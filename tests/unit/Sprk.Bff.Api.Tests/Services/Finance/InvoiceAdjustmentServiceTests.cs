using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 102 (FR-32, FR-07, D-42, D-74, I-1, NFR-09, D-128 / D-129 / D-130): <see cref="InvoiceAdjustmentService"/>. It runs over the REAL
/// <see cref="InvoiceTotalsService"/> (016) with the Dataverse boundary modelled in memory (row versions, a conditional deactivation, inactive
/// rows that the query's own filter must leave out), so each test proves the stored state (the row, <c>sprk_approvedtotal</c>), not the calls the
/// service makes.
/// </summary>
public sealed class InvoiceAdjustmentServiceTests
{
    private static readonly Guid Reviewer = Guid.Parse("02000000-0000-4000-8000-000000000001");
    private static readonly Guid Remover = Guid.Parse("02000000-0000-4000-8000-000000000002");
    private static readonly Guid Core = Guid.Parse("02000000-0000-4000-8000-0000000000c0");
    private static readonly Guid Firm = Guid.Parse("02000000-0000-4000-8000-0000000000f1");
    private static readonly Guid Team = Guid.Parse("02000000-0000-4000-8000-0000000000d0");
    private static readonly Guid RuleSet = Guid.Parse("02000000-0000-4000-8000-0000000000a1");
    private const string Why = "Volume discount agreed in the engagement letter";

    private sealed class InMemoryLocks : IIdempotencyService
    {
        private readonly ConcurrentDictionary<string, string> _held = new();

        public int Acquired;

        public void Hold(string key) => _held[key] = key;

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            var granted = _held.TryAdd(eventId, eventId);
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
        public readonly Entity Invoice;
        public readonly List<Entity> Lines = [];
        public readonly InMemoryLocks Locks = new();
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<IFieldMappingDataverseService> Records = new();
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();
        public readonly List<BillingRuleCandidate> Candidates = [];
        public readonly Dictionary<Guid, string> RuleSetNames = new() { [RuleSet] = "Acme Billing Guidelines" };
        public readonly List<Guid> FirmsAsked = [];
        public RecordOwnerResolution Owner = RecordOwnerResolution.Owned(Team);
        public int CreateCount;
        public int DeactivationCount;
        public int TotalsWriteCount;
        public Exception? TotalsFault;
        public Exception? CreateFault;
        public Action? AfterCreate;
        public Action? AfterDeactivate;
        private long _nextVersion = 100;

        public World(decimal billedTotal = 1100m, int status = InvoiceStatusCodes.InReview, bool withLines = true)
        {
            Invoice = new Entity("sprk_invoice", InvoiceId)
            {
                ["statuscode"] = new OptionSetValue(status),
                ["sprk_totalamount"] = new Money(billedTotal),
                ["sprk_invoicedate"] = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
                ["createdon"] = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
                ["sprk_vendororg"] = new EntityReference("sprk_organization", Firm),
                ["sprk_project"] = new EntityReference("sprk_project", Core),
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid()),
                ["sprk_regardingrecordid"] = Core.ToString("D"),
                ["sprk_regardingrecordname"] = "Project",
                ["sprk_regardingrecordurl"] = "https://example.invalid/p",
                ["sprk_regardingrecordnumber"] = "P-1",
            };

            if (withLines)
            {
                // Fees 1,000 (800 + 200 reduced to 100: a line adjustment of 100) and expenses 100: billed 1,100.
                Lines.Add(Line(InvoiceLineCostType.Fee, 800m, InvoiceLineStatus.Accepted, approved: 800m, sequence: 1));
                Lines.Add(Line(InvoiceLineCostType.Fee, 200m, InvoiceLineStatus.Reduced, approved: 100m, sequence: 2));
                Lines.Add(Line(InvoiceLineCostType.Expense, 100m, InvoiceLineStatus.Accepted, approved: 100m, sequence: 3));
            }

            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Owner);

            // Reads. The 'not an inactive Adjustment' filter is modelled as Dataverse evaluates it, so a query that does NOT carry it returns the
            // inactive rows and the tests fail; a row is also returned by id in any state (the service's own repeat / remove read).
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) =>
                {
                    lock (Lines)
                    {
                        return Task.FromResult(query.EntityName switch
                        {
                            "sprk_invoice" => new EntityCollection([Copy(Invoice)]),
                            "sprk_billingevent" => new EntityCollection(LinesFor(query).Select(Copy).ToList()),
                            "sprk_ruleset" => new EntityCollection(RuleSetNames
                                .Select(n => new Entity("sprk_ruleset", n.Key) { ["sprk_name"] = n.Value }).ToList()),
                            _ => new EntityCollection(),
                        });
                    }
                });

            Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    if (CreateFault is not null)
                    {
                        return Task.FromException(CreateFault);
                    }

                    lock (Lines)
                    {
                        foreach (var create in requests.OfType<CreateRequest>())
                        {
                            var row = Copy(create.Target);
                            row["versionnumber"] = ++_nextVersion;
                            row["statecode"] = new OptionSetValue(0);
                            Lines.Add(row);
                            CreateCount++;
                        }
                    }

                    AfterCreate?.Invoke();
                    return Task.CompletedTask;
                });

            Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                    It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken _) =>
                {
                    lock (Lines)
                    {
                        if (updates.Any(u => Lines.Single(l => l.Id == u.id).GetAttributeValue<long>("versionnumber") != u.rowVersion))
                        {
                            throw new DBConcurrencyException("simulated row version mismatch: nothing applied");
                        }

                        foreach (var (id, fields, _) in updates)
                        {
                            Apply(Lines.Single(l => l.Id == id), fields);
                            DeactivationCount++;
                        }
                    }

                    AfterDeactivate?.Invoke();
                    return Task.CompletedTask;
                });

            Entities.Setup(e => e.UpdateAsync("sprk_billingevent", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    lock (Lines)
                    {
                        Apply(Lines.Single(l => l.Id == id), fields);
                        DeactivationCount++;
                    }

                    return Task.CompletedTask;
                });

            // 016's reads and writes, over the same rows.
            Records.Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, string[] _, CancellationToken token) => TotalsFault is not null
                    ? Task.FromException<Dictionary<string, object?>>(TotalsFault)
                    : token.IsCancellationRequested
                        ? Task.FromCanceled<Dictionary<string, object?>>(token)
                        : Task.FromResult(new Dictionary<string, object?>
                    {
                        ["sprk_totalamount"] = Invoice.GetAttributeValue<Money>("sprk_totalamount").Value,
                        ["versionnumber"] = 5L,
                    }));
            Records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 5L, It.IsAny<CancellationToken>()))
                .Callback((string _, Guid _, Dictionary<string, object?> fields, long _, CancellationToken _) =>
                {
                    TotalsWriteCount++;
                    foreach (var (name, value) in fields)
                    {
                        Invoice[name] = new Money((decimal)value!);
                    }
                })
                .Returns(Task.CompletedTask);
            Records.Setup(r => r.UpdateExistingRecordFieldsAsync("sprk_billingevent", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Guid id, Dictionary<string, object?> fields, CancellationToken _) =>
                {
                    lock (Lines)
                    {
                        var line = Lines.Single(l => l.Id == id);
                        foreach (var (name, value) in fields)
                        {
                            line[name] = value;
                        }
                    }
                })
                .Returns(Task.CompletedTask);
        }

        private IEnumerable<Entity> LinesFor(QueryExpression query)
        {
            // By primary key: any state. By invoice: the query's own filter decides whether an inactive Adjustment line is returned.
            var byId = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_billingeventid");
            if (byId is not null)
            {
                return Lines.Where(l => l.Id == (Guid)byId.Values[0]);
            }

            var filtersInactive = query.Criteria.Filters.Any(f => f.Conditions.Any(c => c.AttributeName == "statecode"));
            return Lines.Where(l => l.GetAttributeValue<EntityReference>("sprk_invoice")?.Id == InvoiceId
                && (!filtersInactive || InvoiceTotalsService.IsCounted(l)));
        }

        public InvoiceAdjustmentService Service()
        {
            var totals = new InvoiceTotalsService(Records.Object, Entities.Object, NullLogger<InvoiceTotalsService>.Instance);
            return new InvoiceAdjustmentService(
                Entities.Object, totals, Locks, Ownership.Object,
                (firm, _) =>
                {
                    FirmsAsked.Add(firm);
                    return Task.FromResult<IReadOnlyList<BillingRuleCandidate>>(Candidates.ToList());
                },
                NullLogger<InvoiceAdjustmentService>.Instance,
                new FixedTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
        }

        public Entity Row(Guid id) => Lines.Single(l => l.Id == id);

        public IEnumerable<Entity> Adjustments => Lines.Where(l => l.GetAttributeValue<OptionSetValue>("sprk_costtype")?.Value == (int)InvoiceLineCostType.Adjustment);

        public decimal Stored(string column) => Invoice.GetAttributeValue<Money>(column).Value;

        public Entity Line(InvoiceLineCostType type, decimal amount, InvoiceLineStatus status, decimal? approved, int sequence)
        {
            var line = new Entity("sprk_billingevent", Guid.NewGuid())
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                ["sprk_linesequence"] = sequence,
                ["sprk_costtype"] = new OptionSetValue((int)type),
                ["sprk_amount"] = new Money(amount),
                ["sprk_linestatus"] = new OptionSetValue((int)status),
                ["statecode"] = new OptionSetValue(0),
                ["versionnumber"] = ++_nextVersion,
            };
            if (approved is { } a)
            {
                line["sprk_approvedamount"] = new Money(a);
            }

            return line;
        }

        public Entity InactiveAdjustment(decimal fixedValue)
        {
            var line = new Entity("sprk_billingevent", Guid.NewGuid())
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                ["sprk_linesequence"] = 9,
                ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Adjustment),
                ["sprk_amount"] = new Money(fixedValue),
                ["sprk_adjustmentmethod"] = new OptionSetValue((int)InvoiceAdjustmentMethod.Fixed),
                ["sprk_adjustmentvalue"] = fixedValue,
                ["statecode"] = new OptionSetValue(1),
                ["statuscode"] = new OptionSetValue(2),
                ["versionnumber"] = ++_nextVersion,
            };
            Lines.Add(line);
            return line;
        }

        private static void Apply(Entity row, Dictionary<string, object> fields)
        {
            foreach (var (name, value) in fields)
            {
                row[name] = value;
            }
        }

        private static Entity Copy(Entity source)
        {
            var copy = new Entity(source.LogicalName, source.Id);
            foreach (var (name, value) in source.Attributes)
            {
                copy[name] = value;
            }

            return copy;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AddAdjustmentCommand Add(World w, string method = "percent", decimal? value = 10m, string? reason = Why, Guid? rule = null, Guid? id = null) =>
        new(w.InvoiceId, id ?? Guid.NewGuid(), method, value, reason, rule, Reviewer);

    private static BillingRuleCandidate Rule(
        Guid? version = null, Guid? policy = null, string code = "BILL-DISCOUNT", RuleType type = RuleType.ApprovedRate,
        DateOnly? from = null, DateOnly? to = null, bool current = true, int number = 1, RuleSetScope scope = RuleSetScope.ClientDefault, Guid? ruleSet = null,
        string? ruleTypeText = null) => new()
    {
        PolicyId = policy ?? Guid.NewGuid(),
        PolicyCode = code,
        PolicyVersionId = version ?? Guid.NewGuid(),
        RuleSetId = ruleSet ?? RuleSet,
        Scope = scope,
        RuleType = ruleTypeText ?? ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ClauseReference = "Volume discounts: 5% above 10,000 dollars a month.",
        ClauseLocation = "Section 4.2, page 3",
        ShortHeadline = "Volume discount",
        InForceFrom = from ?? new DateOnly(2026, 1, 1),
        InForceTo = to,
        IsCurrent = current,
        VersionNumber = number,
    };

    // =========================================================================================
    // Add: the amount and the totals are 016's (FR-32, FR-07, D-42, I-1, D-130)
    // =========================================================================================

    [Fact]
    public async Task TenPercent_TakesTenPercentOfFeesAfterLineAdjustments_AndLeavesExpensesAlone()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, "percent", 10m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        // Fees 1,000, the Reduced line took 100 off, so the base is 900 and 10% is 90. Expenses (100) are not in the base.
        result.Adjustment!.Amount.Should().Be(90m);
        result.Totals!.Fees.Should().Be(1000m);
        result.Totals.Expenses.Should().Be(100m);
        result.Totals.LineAdjustments.Should().Be(100m);
        result.Totals.InvoiceAdjustments.Should().Be(90m);
        result.Totals.Approved.Should().Be(910m);
        // D-130: the STORED approved total moved (nothing else recalculates it after submission).
        w.Stored("sprk_approvedtotal").Should().Be(910m);
        w.Stored("sprk_invoiceadjustmentstotal").Should().Be(90m);
        w.Stored("sprk_expensestotal").Should().Be(100m);
        w.Adjustments.Should().ContainSingle().Which.GetAttributeValue<Money>("sprk_amount").Value.Should().Be(90m);
    }

    [Fact]
    public async Task FixedAmount_ReducesTheApprovedTotalByThatAmount()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, "fixed", 150m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        result.Adjustment!.Amount.Should().Be(150m);
        w.Stored("sprk_approvedtotal").Should().Be(850m);
    }

    [Fact]
    public async Task TheRow_IsAnAdjustmentLine_OfTheInvoice_WithItsCoreRecordFieldsOwnerAndAttribution()
    {
        var w = new World();
        var rule = Rule();
        w.Candidates.Add(rule);

        var result = await w.Service().AddAsync(Add(w, "percent", 12.5m, rule: rule.PolicyVersionId), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        var row = w.Row(result.Adjustment!.LineId);
        row.GetAttributeValue<OptionSetValue>("sprk_costtype").Value.Should().Be((int)InvoiceLineCostType.Adjustment);
        row.GetAttributeValue<OptionSetValue>("sprk_adjustmentmethod").Value.Should().Be((int)InvoiceAdjustmentMethod.Percent);
        row.GetAttributeValue<decimal>("sprk_adjustmentvalue").Should().Be(12.5m);
        row.GetAttributeValue<string>("sprk_adjustmentreason").Should().Be(Why);
        row.GetAttributeValue<EntityReference>("sprk_adjustmentrule").Should().Be(new EntityReference("sprk_policyversion", rule.PolicyVersionId));
        row.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(w.InvoiceId);
        row.GetAttributeValue<EntityReference>("sprk_reviewedby").Should().Be(new EntityReference("systemuser", Reviewer));
        row.GetAttributeValue<DateTime>("sprk_reviewedon").Should().Be(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        row.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Accepted);
        row.GetAttributeValue<int>("sprk_linesequence").Should().Be(4, "after the invoice's three lines");

        // 013: exactly one core-record lookup, plus the resolver fields copied from the invoice; the firm; the owner; the creator person.
        row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(Core);
        row.Contains("sprk_matter").Should().BeFalse();
        row.Contains("sprk_regardingworkassignment").Should().BeFalse();
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(Core.ToString("D"));
        row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Should().Be(w.Invoice.GetAttributeValue<EntityReference>("sprk_regardingrecordtype"));
        row.GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(Firm);
        row.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("team", Team));
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Should().Be(new EntityReference("systemuser", Reviewer));
    }

    [Fact]
    public async Task AFixedAdjustmentThatTakesTheApprovedTotalToExactlyZero_IsAllowed()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, "fixed", 1000m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied, "the floor is below zero, not at zero");
        w.Stored("sprk_approvedtotal").Should().Be(0m);
    }

    [Fact]
    public async Task OneHundredPercent_TakesTheWholeFeeBaseAfterLineAdjustments_AndLeavesTheExpenses()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, "percent", 100m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        result.Adjustment!.Amount.Should().Be(900m);
        w.Stored("sprk_approvedtotal").Should().Be(100m, "1,100 billed - 100 line adjustment - 900 = the 100 of expenses");
    }

    [Fact]
    public async Task AnAdjustmentThatWouldMakeTheApprovedTotalNegative_Is400_AndWritesNothing()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, "fixed", 1000.01m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.NegativeTotal);
        result.Code.Should().Be("ebilling.adjustment.negative_total");
        w.CreateCount.Should().Be(0);
        w.Adjustments.Should().BeEmpty();
        w.TotalsWriteCount.Should().Be(0);
        w.Invoice.Contains("sprk_approvedtotal").Should().BeFalse("nothing was written");
    }

    [Fact]
    public async Task ASecondAdjustment_ThatTogetherWithTheFirstWouldGoNegative_IsRefused_AndTheFirstStays()
    {
        var w = new World();
        var service = w.Service();
        (await service.AddAsync(Add(w, "fixed", 1000m), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);

        var second = await service.AddAsync(Add(w, "fixed", 200m), CancellationToken.None);

        second.Status.Should().Be(AdjustmentStatus.NegativeTotal);
        w.Adjustments.Should().ContainSingle();
        w.Stored("sprk_approvedtotal").Should().Be(0m);
    }

    [Fact]
    public async Task ACreditMemo_HasNoExtraLimit_D129()
    {
        // D-129 (1): the FR-32 floor applies only to an invoice billed at zero or more. A credit memo (billed -500) takes a fixed adjustment
        // and stays negative; this task adds no refusal of a negative line or total.
        var w = new World(billedTotal: -500m, withLines: false);
        w.Lines.Add(w.Line(InvoiceLineCostType.Fee, -500m, InvoiceLineStatus.Accepted, approved: -500m, sequence: 1));

        var result = await w.Service().AddAsync(Add(w, "fixed", 100m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        w.Stored("sprk_approvedtotal").Should().Be(-600m);
    }

    [Fact]
    public async Task APercentAdjustmentOnANetNegativeFeeBase_AdjustsNothing_D129()
    {
        var w = new World(billedTotal: -700m, withLines: false);
        w.Lines.Add(w.Line(InvoiceLineCostType.Fee, -700m, InvoiceLineStatus.Accepted, approved: -700m, sequence: 1));

        var result = await w.Service().AddAsync(Add(w, "percent", 10m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        result.Adjustment!.Amount.Should().Be(0m);
        w.Stored("sprk_approvedtotal").Should().Be(-700m);
    }

    // =========================================================================================
    // Add: the request (400s), the invoice (404 / 409)
    // =========================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingReason_Is400_AndReadsNothing(string? reason)
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, reason: reason), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.adjustment.reason_required");
        w.Locks.Acquired.Should().Be(0);
        w.Entities.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AReasonOverTheLimit_Is400()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, reason: new string('x', InvoiceAdjustmentService.ReasonMaxLength + 1)), CancellationToken.None);

        result.Code.Should().Be("ebilling.adjustment.reason_too_long");
        w.CreateCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("amount")]
    [InlineData("percentage-ish")]
    public async Task AnUnknownMethod_Is400(string? method)
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, method!), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.adjustment.method_invalid");
    }

    [Theory]
    [InlineData("percent", null)]
    [InlineData("percent", 0.0)]
    [InlineData("percent", -5.0)]
    [InlineData("percent", 100.01)]
    [InlineData("percent", 10.005)]
    [InlineData("fixed", 0.0)]
    [InlineData("fixed", -1.0)]
    [InlineData("fixed", 12.345)]
    [InlineData("fixed", 1000000000.01)]
    public async Task AValueOutsideTheRange_Is400(string method, double? value)
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w, method, value is null ? null : (decimal)value), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.adjustment.value_invalid");
        w.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task TheBoundaryValues_AreAccepted()
    {
        var w = new World(billedTotal: 1100m);
        var service = w.Service();

        (await service.AddAsync(Add(w, "percent", 100m), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        (await service.AddAsync(Add(w, "fixed", 0.01m), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
    }

    [Fact]
    public async Task EmptyIds_AreRefused_BeforeAnyRead()
    {
        var w = new World();
        var service = w.Service();

        (await service.AddAsync(Add(w, id: Guid.Empty), CancellationToken.None)).Code.Should().Be("ebilling.request.invalid");
        (await service.AddAsync(Add(w, rule: Guid.Empty), CancellationToken.None)).Code.Should().Be("ebilling.request.invalid");
        (await service.AddAsync(Add(w) with { ReviewerSystemUserId = Guid.Empty }, CancellationToken.None)).Code.Should().Be("ebilling.request.invalid");
        w.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceStatusCodes.SentToAp)]
    [InlineData(InvoiceStatusCodes.Rejected)]
    public async Task NotInReview_Is409_AndWritesNothing(int status)
    {
        var w = new World(status: status);

        var result = await w.Service().AddAsync(Add(w), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.NotInReview);
        result.Code.Should().Be("ebilling.invoice.not_in_review");
        w.CreateCount.Should().Be(0);
        w.TotalsWriteCount.Should().Be(0);
    }

    [Fact]
    public async Task AnInvoiceThatIsNotThere_IsNotFound()
    {
        var w = new World();
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_invoice"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var result = await w.Service().AddAsync(Add(w), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvoiceNotFound);
        w.CreateCount.Should().Be(0);
        (await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.InvoiceNotFound);
        (await w.Service().RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, Guid.NewGuid(), Remover), CancellationToken.None)).Status
            .Should().Be(AdjustmentStatus.InvoiceNotFound);
    }

    [Fact]
    public async Task AnInvoiceBeingChangedByAnotherRequest_Is409Busy_AndReadsNothing()
    {
        var w = new World();
        w.Locks.Hold(InvoiceLineDecisionService.LockKeyPrefix + w.InvoiceId.ToString("N"));

        var result = await w.Service().AddAsync(Add(w), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Busy);
        result.Code.Should().Be("ebilling.invoice.busy");
        w.Entities.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheLock_IsReleased_AfterTheRequest()
    {
        var w = new World();
        var service = w.Service();

        await service.AddAsync(Add(w), CancellationToken.None);
        var second = await service.AddAsync(Add(w, "fixed", 5m), CancellationToken.None);

        second.Status.Should().Be(AdjustmentStatus.Applied);
        w.Locks.Acquired.Should().Be(2);
    }

    [Fact]
    public async Task AnInvoiceWithNoResolvedOwner_IsRefused_AndWritesNothing()
    {
        var w = new World { Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team") };

        var result = await w.Service().AddAsync(Add(w), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.OwnerUnresolved);
        w.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task ARead_ThatFails_ChangesNothing()
    {
        var w = new World();
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        var result = await w.Service().AddAsync(Add(w), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Unavailable);
        w.CreateCount.Should().Be(0);
    }

    // =========================================================================================
    // Add: the cited rule (F-8, D-74)
    // =========================================================================================

    [Fact]
    public async Task ARuleThatIsNotAnOption_Is400_AndWritesNothing()
    {
        var w = new World();
        w.Candidates.Add(Rule());

        var result = await w.Service().AddAsync(Add(w, rule: Guid.NewGuid()), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.adjustment.rule_not_allowed");
        w.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task ARuleThatIsNotInForceOnTheInvoiceDate_IsNotAnOption()
    {
        var w = new World();
        var future = Rule(from: new DateOnly(2026, 10, 1)); // the invoice is dated 2026-09-15
        w.Candidates.Add(future);

        var result = await w.Service().AddAsync(Add(w, rule: future.PolicyVersionId), CancellationToken.None);

        result.Code.Should().Be("ebilling.adjustment.rule_not_allowed");
    }

    [Fact]
    public async Task AnAnnotationOnly_NoRule_NeedsNoRuleCheck()
    {
        var w = new World();

        var result = await w.Service().AddAsync(Add(w), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        w.FirmsAsked.Should().BeEmpty("the rules are read only when a rule is cited");
        w.Adjustments.Single().Contains("sprk_adjustmentrule").Should().BeFalse();
    }

    // =========================================================================================
    // Remove (NFR-09): deactivated, never deleted; totals restored; D-130
    // =========================================================================================

    [Fact]
    public async Task Remove_DeactivatesTheRow_RestoresTheTotals_AndKeepsTheRow()
    {
        var w = new World();
        var service = w.Service();
        var added = await service.AddAsync(Add(w, "percent", 10m), CancellationToken.None);
        w.Stored("sprk_approvedtotal").Should().Be(910m);

        var removed = await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, added.Adjustment!.LineId, Remover), CancellationToken.None);

        removed.Status.Should().Be(AdjustmentStatus.Applied);
        removed.RemovedLineId.Should().Be(added.Adjustment.LineId);
        removed.Totals!.Approved.Should().Be(1000m);
        removed.Totals.InvoiceAdjustments.Should().Be(0m);
        w.Stored("sprk_approvedtotal").Should().Be(1000m, "D-130: the stored approved total follows the removal");
        w.Stored("sprk_invoiceadjustmentstotal").Should().Be(0m);
        var row = w.Row(added.Adjustment.LineId);
        row.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1);
        row.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(2);
        row.GetAttributeValue<EntityReference>("sprk_reviewedby").Should().Be(new EntityReference("systemuser", Remover));
        w.Lines.Should().Contain(l => l.Id == added.Adjustment.LineId, "never deleted");
        w.Entities.Verify(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_Twice_ChangesNothingTheSecondTime()
    {
        var w = new World();
        var service = w.Service();
        var added = await service.AddAsync(Add(w, "fixed", 50m), CancellationToken.None);
        var command = new RemoveAdjustmentCommand(w.InvoiceId, added.Adjustment!.LineId, Remover);
        await service.RemoveAsync(command, CancellationToken.None);
        var deactivations = w.DeactivationCount;

        var again = await service.RemoveAsync(command, CancellationToken.None);

        again.Status.Should().Be(AdjustmentStatus.Applied);
        again.AlreadyApplied.Should().BeTrue();
        w.DeactivationCount.Should().Be(deactivations);
        w.Stored("sprk_approvedtotal").Should().Be(1000m);
    }

    [Fact]
    public async Task AnInactiveAdjustmentLine_IsNotCounted_ByTheDryRunNorTheStoredTotals()
    {
        // 016's totals and 100's read model leave a removed Adjustment line out; without that a removed 400 would still sit in every total.
        var w = new World();
        w.InactiveAdjustment(400m);

        var result = await w.Service().AddAsync(Add(w, "fixed", 50m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        w.Stored("sprk_invoiceadjustmentstotal").Should().Be(50m);
        w.Stored("sprk_approvedtotal").Should().Be(950m);
    }

    [Fact]
    public async Task Remove_OfALineThatIsNotAnAdjustment_OrNotTheInvoices_IsTheUniformNotFound()
    {
        var w = new World();
        var service = w.Service();
        var fee = w.Lines.First(l => l.GetAttributeValue<OptionSetValue>("sprk_costtype").Value == (int)InvoiceLineCostType.Fee);
        var foreign = w.Line(InvoiceLineCostType.Adjustment, 10m, InvoiceLineStatus.Accepted, null, 7);
        foreign["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
        w.Lines.Add(foreign);

        (await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, fee.Id, Remover), CancellationToken.None)).Status
            .Should().Be(AdjustmentStatus.AdjustmentNotFound);
        (await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, foreign.Id, Remover), CancellationToken.None)).Status
            .Should().Be(AdjustmentStatus.AdjustmentNotFound);
        (await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, Guid.NewGuid(), Remover), CancellationToken.None)).Status
            .Should().Be(AdjustmentStatus.AdjustmentNotFound);
        w.DeactivationCount.Should().Be(0);
        fee.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
        foreign.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
    }

    [Fact]
    public async Task Remove_NotInReview_Is409()
    {
        var w = new World(status: InvoiceStatusCodes.Approved);
        var adjustment = w.Line(InvoiceLineCostType.Adjustment, 10m, InvoiceLineStatus.Accepted, null, 7);
        w.Lines.Add(adjustment);

        var result = await w.Service().RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, adjustment.Id, Remover), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.NotInReview);
        w.DeactivationCount.Should().Be(0);
    }

    [Fact]
    public async Task Remove_WhenTheRowChangedSinceItWasRead_Is409_AndChangesNothing()
    {
        var w = new World();
        var adjustment = w.Line(InvoiceLineCostType.Adjustment, 10m, InvoiceLineStatus.Accepted, null, 7);
        adjustment["sprk_adjustmentmethod"] = new OptionSetValue((int)InvoiceAdjustmentMethod.Fixed);
        adjustment["sprk_adjustmentvalue"] = 10m;
        w.Lines.Add(adjustment);
        var service = w.Service();
        // Another writer bumps the row after the service read its version: the conditional write is refused.
        w.Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException("changed"));

        var result = await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, adjustment.Id, Remover), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Changed);
        result.Code.Should().Be("ebilling.line.changed");
        adjustment.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
        w.TotalsWriteCount.Should().Be(0);
    }

    // =========================================================================================
    // Idempotence and recovery
    // =========================================================================================

    [Fact]
    public async Task ARepeatOfTheSameRequest_AddsNothingTwice()
    {
        var w = new World();
        var service = w.Service();
        var command = Add(w, "percent", 10m);
        var first = await service.AddAsync(command, CancellationToken.None);

        var repeat = await service.AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        repeat.Adjustment!.LineId.Should().Be(first.Adjustment!.LineId);
        repeat.Adjustment.Amount.Should().Be(90m);
        w.Adjustments.Should().ContainSingle();
        w.CreateCount.Should().Be(1);
        w.Stored("sprk_approvedtotal").Should().Be(910m);
    }

    [Fact]
    public async Task TheSameIdWithOtherContent_Is409IdReused_AndChangesNothing()
    {
        var w = new World();
        var service = w.Service();
        var id = Guid.NewGuid();
        await service.AddAsync(Add(w, "percent", 10m, id: id), CancellationToken.None);

        var other = await service.AddAsync(Add(w, "percent", 20m, id: id), CancellationToken.None);

        other.Status.Should().Be(AdjustmentStatus.IdReused);
        other.Code.Should().Be("ebilling.adjustment.id_reused");
        w.Stored("sprk_approvedtotal").Should().Be(910m);
    }

    [Fact]
    public async Task TheSameIdAfterTheAdjustmentWasRemoved_Is409IdReused_NotAResurrection()
    {
        var w = new World();
        var service = w.Service();
        var command = Add(w, "fixed", 40m);
        var added = await service.AddAsync(command, CancellationToken.None);
        await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, added.Adjustment!.LineId, Remover), CancellationToken.None);

        var again = await service.AddAsync(command, CancellationToken.None);

        again.Status.Should().Be(AdjustmentStatus.IdReused);
        w.Stored("sprk_approvedtotal").Should().Be(1000m);
        w.CreateCount.Should().Be(1);
    }

    [Fact]
    public void TheSameAdjustmentId_OnAnotherInvoice_IsAnotherRow()
    {
        var id = Guid.NewGuid();
        var invoice = Guid.NewGuid();

        InvoiceAdjustmentService.LineIdFor(Guid.NewGuid(), id).Should().NotBe(InvoiceAdjustmentService.LineIdFor(invoice, id));
        InvoiceAdjustmentService.LineIdFor(invoice, id).Should().Be(InvoiceAdjustmentService.LineIdFor(invoice, id));
    }

    [Fact]
    public async Task ATotalsFailureAfterTheWrite_Is503Incomplete_AndARepeatFinishesIt()
    {
        var w = new World();
        var service = w.Service();
        var command = Add(w, "percent", 10m);
        w.TotalsFault = new TimeoutException("totals");

        var failed = await service.AddAsync(command, CancellationToken.None);

        failed.Status.Should().Be(AdjustmentStatus.Incomplete);
        failed.Code.Should().Be("ebilling.adjustment.incomplete");
        w.Adjustments.Should().ContainSingle("the line was written");
        w.Invoice.Contains("sprk_approvedtotal").Should().BeFalse();

        w.TotalsFault = null;
        var repeat = await service.AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        w.Adjustments.Should().ContainSingle("a repeat adds no second line");
        w.Stored("sprk_approvedtotal").Should().Be(910m);
    }

    [Fact]
    public async Task ARequestCancelledAfterTheCreate_StillStoresTheTotals_D130()
    {
        // The reviewer closes the tab after the line committed: the totals step must still run, or the stored total omits an active adjustment.
        var w = new World();
        using var cts = new CancellationTokenSource();
        w.AfterCreate = cts.Cancel;

        var result = await w.Service().AddAsync(Add(w, "percent", 10m), cts.Token);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        w.Adjustments.Should().ContainSingle();
        w.Stored("sprk_approvedtotal").Should().Be(910m);
    }

    [Fact]
    public async Task ARequestCancelledAfterTheRemoval_StillStoresTheTotals_D130()
    {
        var w = new World();
        var service = w.Service();
        var added = await service.AddAsync(Add(w, "fixed", 50m), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        w.AfterDeactivate = cts.Cancel;

        var result = await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, added.Adjustment!.LineId, Remover), cts.Token);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        w.Stored("sprk_approvedtotal").Should().Be(1000m, "the removed 50 is added back");
    }

    [Fact]
    public async Task ATotalsFailureAfterTheRemoval_SaysTheAdjustmentWasRemoved_NotSaved()
    {
        var w = new World();
        var service = w.Service();
        var added = await service.AddAsync(Add(w, "fixed", 50m), CancellationToken.None);
        w.TotalsFault = new TimeoutException("totals");

        var failed = await service.RemoveAsync(new RemoveAdjustmentCommand(w.InvoiceId, added.Adjustment!.LineId, Remover), CancellationToken.None);

        failed.Status.Should().Be(AdjustmentStatus.Incomplete);
        failed.Detail.Should().Contain("removed").And.NotContain("saved");
    }

    [Fact]
    public async Task ACreateFailure_Is503_AndARepeatAddsTheLineOnce()
    {
        var w = new World();
        var service = w.Service();
        var command = Add(w, "fixed", 25m);
        w.CreateFault = new TimeoutException("create");

        (await service.AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Unavailable);
        w.Adjustments.Should().BeEmpty();

        w.CreateFault = null;
        (await service.AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        w.Adjustments.Should().ContainSingle();
    }

    [Fact]
    public async Task IfTheTotalsAreRefusedAfterTheWrite_TheNewLineIsDeactivated_NothingStaysHalfApplied()
    {
        // The dry run passed; then another writer lowered the billed total, so 016's recalculation refuses (FR-32). The line is undone.
        var w = new World();
        w.AfterCreate = () => w.Invoice["sprk_totalamount"] = new Money(100m);

        var result = await w.Service().AddAsync(Add(w, "fixed", 150m), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.NegativeTotal);
        var row = w.Adjustments.Should().ContainSingle().Subject;
        row.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "the line the refused calculation counted is deactivated");
        w.TotalsWriteCount.Should().Be(0);
    }

    // =========================================================================================
    // The rule options (F-8, D-74, FR-24)
    // =========================================================================================

    [Fact]
    public async Task Options_AreThePublishedRulesInForceOnTheInvoiceDate_WithNameVersionAndClause()
    {
        var w = new World();
        var inForce = Rule(code: "BILL-A", number: 2, scope: RuleSetScope.Firm);
        w.Candidates.Add(inForce);

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        result.AsOf.Should().Be(new DateOnly(2026, 9, 15));
        var option = result.Rules.Should().ContainSingle().Subject;
        option.RuleId.Should().Be(inForce.PolicyVersionId);
        option.RuleSetName.Should().Be("Acme Billing Guidelines");
        option.RuleSetId.Should().Be(RuleSet);
        option.Version.Should().Be(2);
        option.Scope.Should().Be("Firm");
        option.ClauseReference.Should().Be("Volume discounts: 5% above 10,000 dollars a month.");
        option.ClauseLocation.Should().Be("Section 4.2, page 3");
        option.PolicyCode.Should().Be("BILL-A");
        w.FirmsAsked.Should().Equal([Firm], "the invoice's own firm is the one whose rule sets apply");
    }

    [Fact]
    public async Task Options_CarryNoDocumentLink()
    {
        var w = new World();
        w.Candidates.Add(Rule());

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        var json = JsonSerializer.Serialize(result.Rules!.Single()).ToLowerInvariant();
        json.Should().NotContain("document").And.NotContain("url").And.NotContain("source").And.NotContain("link");
    }

    [Fact]
    public async Task Options_LeaveOutADraft_AFutureVersion_ASupersededVersionOutsideItsWindow_AndPickTheVersionInForce()
    {
        var w = new World();
        var policy = Guid.NewGuid();
        var superseded = Rule(policy: policy, code: "BILL-P", current: false, from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 9, 1), number: 1); // ended before 09-15
        var inForceOnDate = Rule(policy: policy, code: "BILL-P", current: true, from: new DateOnly(2026, 9, 1), number: 2);
        var future = Rule(policy: Guid.NewGuid(), code: "BILL-FUTURE", from: new DateOnly(2026, 12, 1));
        var draft = Rule(policy: Guid.NewGuid(), code: "BILL-DRAFT", current: false, to: null); // neither current nor ended: an abandoned edit
        w.Candidates.AddRange([superseded, inForceOnDate, future, draft]);

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Rules.Should().ContainSingle().Which.RuleId.Should().Be(inForceOnDate.PolicyVersionId);
    }

    [Fact]
    public async Task Options_ASupersededVersionStillInItsWindow_IsTheOneInForce()
    {
        var w = new World();
        var policy = Guid.NewGuid();
        var old = Rule(policy: policy, current: false, from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 10, 1), number: 1);
        var next = Rule(policy: policy, current: true, from: new DateOnly(2026, 10, 1), number: 2);
        w.Candidates.AddRange([old, next]); // the invoice is dated 2026-09-15

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Rules.Should().ContainSingle().Which.RuleId.Should().Be(old.PolicyVersionId);
    }

    [Theory]
    [InlineData("999999")] // a number the closed set does not name
    [InlineData("Quantum")] // a name it does not have
    [InlineData("")]
    [InlineData(null)]
    public async Task Options_AnUnknownRuleType_IsNeverOffered_FailClosed_FR24(string? type)
    {
        var w = new World();
        var unknown = Rule(ruleTypeText: type ?? string.Empty);
        var known = Rule(code: "BILL-OK");
        w.Candidates.AddRange([unknown, known]);

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Rules.Should().ContainSingle().Which.RuleId.Should().Be(known.PolicyVersionId);
    }

    [Fact]
    public async Task Options_ASystemRuleAndAnOntologyRule_AreNeverOffered()
    {
        var w = new World();
        var system = Rule(type: RuleType.SubmissionIntegrity, code: "BILL-SYSTEM");
        var ontology = Rule(type: RuleType.Threshold, code: "ONT-THRESHOLD");
        var billing = Rule(type: RuleType.Discount, code: "BILL-DISCOUNT");
        w.Candidates.AddRange([system, ontology, billing]);

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Rules.Should().ContainSingle().Which.RuleId.Should().Be(billing.PolicyVersionId);
    }

    [Fact]
    public async Task Options_AreOrderedByRuleSetThenCode()
    {
        var w = new World();
        var other = Guid.NewGuid();
        w.RuleSetNames[other] = "Aardvark Rate Card";
        w.Candidates.AddRange([Rule(code: "B"), Rule(code: "A"), Rule(code: "Z", ruleSet: other)]);

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Rules!.Select(r => r.PolicyCode).Should().Equal("Z", "A", "B");
    }

    [Fact]
    public async Task Options_WithNoInvoiceDate_UseTheDayTheInvoiceWasCreated()
    {
        var w = new World();
        w.Invoice.Attributes.Remove("sprk_invoicedate");
        var byCreation = Rule(from: new DateOnly(2026, 10, 1)); // in force from the creation day (2026-10-01), not on 2026-09-15
        w.Candidates.Add(byCreation);

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.AsOf.Should().Be(new DateOnly(2026, 10, 1));
        result.Rules.Should().ContainSingle();
    }

    [Fact]
    public async Task Options_WithNeitherDate_AreEmpty_AndNothingIsLoaded()
    {
        var w = new World();
        w.Invoice.Attributes.Remove("sprk_invoicedate");
        w.Invoice.Attributes.Remove("createdon");
        w.Candidates.Add(Rule());

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        result.Rules.Should().BeEmpty();
        w.FirmsAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task Options_AreListedInAnyInvoiceStatus()
    {
        var w = new World(status: InvoiceStatusCodes.Approved);
        w.Candidates.Add(Rule());

        var result = await w.Service().ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        result.Rules.Should().ContainSingle();
    }

    [Fact]
    public async Task Options_ALoadFault_Is503_NotAnEmptyList()
    {
        var w = new World();
        var service = new InvoiceAdjustmentService(
            w.Entities.Object, Mock.Of<IInvoiceTotalsService>(), w.Locks, w.Ownership.Object,
            (_, _) => Task.FromException<IReadOnlyList<BillingRuleCandidate>>(new TimeoutException()), NullLogger<InvoiceAdjustmentService>.Instance);

        var result = await service.ListRulesAsync(w.InvoiceId, CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Unavailable);
        result.Rules.Should().BeNull();
    }
}
