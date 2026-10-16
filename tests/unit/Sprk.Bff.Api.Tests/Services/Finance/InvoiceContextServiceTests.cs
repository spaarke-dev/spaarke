using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="InvoiceContextService"/> (ebilling-r1 task 106, FR-33): each of the five sections from fixture data, scoped to the invoice's own
/// firm and core record; one source failing (or timing out) leaves the others intact; a Work Assignment's budget is "not tracked"; the
/// current invoice is excluded from the history; the query count does not depend on the invoice's line count.
/// </summary>
/// <remarks>
/// The Dataverse seam is <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>. A recording fake
/// answers each query from fixture rows by the query's shape and keeps every query so a test can assert what the service asked for (the
/// firm / core-record / not-this-invoice conditions are the scoping, so they are asserted on the query, not assumed from the rows).
/// </remarks>
public class InvoiceContextServiceTests
{
    private static readonly Guid InvoiceId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid FirmId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid MatterId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid WorkAssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000aa01");
    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000aa02");

    private const int Partner = 100000001;
    private const int Associate = 100000003;

    public enum Kind { Header, Core, Budget, Prior, Disallow, FeeLines, Roster, Rates, PreviousLines }

    // =========================================================================================
    // Fixture harness
    // =========================================================================================

    private sealed class Harness
    {
        public Entity Header = InvoiceHeader(matter: MatterId);
        public Entity? CoreRow = MatterCore();
        public Entity? BudgetRow = MatterBudget(budget: 25000m, spend: 10000m, remaining: 15000m, utilization: 40m, count: 5);
        public List<Entity> Prior = [];
        public List<Entity> DisallowLines = [];
        public List<Entity> FeeLines = [];
        public List<Entity> RosterRows = [];
        public List<Entity> RateRows = [];
        public List<Entity> PreviousLines = [];

        public List<QueryExpression> Queries { get; } = [];
        public Func<Kind, QueryExpression, CancellationToken, Task<EntityCollection>?>? Intercept { get; set; }
        public bool DisallowNeverEnds { get; set; }

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);

        public Harness()
        {
            Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken ct) => Answer(query, ct));
        }

        public InvoiceContextService Service(TimeSpan? deadline = null)
        {
            var service = new InvoiceContextService(Entities.Object, NullLogger<InvoiceContextService>.Instance);
            if (deadline is { } d)
            {
                service.Deadline = d;
            }

            return service;
        }

        public int Count(Kind kind) => Queries.Count(q => Classify(q) == kind);

        private Task<EntityCollection> Answer(QueryExpression query, CancellationToken ct)
        {
            lock (Queries)
            {
                Queries.Add(query);
            }

            var kind = Classify(query);
            if (Intercept?.Invoke(kind, query, ct) is { } intercepted)
            {
                return intercepted;
            }

            List<Entity> rows = kind switch
            {
                Kind.Header => [Header],
                Kind.Core => CoreRow is null ? [] : [CoreRow],
                Kind.Budget => BudgetRow is null ? [] : [BudgetRow],
                Kind.Prior => Prior,
                Kind.Disallow => DisallowLines,
                Kind.FeeLines => FeeLines,
                Kind.Roster => RosterRows,
                Kind.Rates => RateRows,
                Kind.PreviousLines => PreviousLines,
                _ => throw new InvalidOperationException("Unclassified query."),
            };

            return Task.FromResult(new EntityCollection(rows)
            {
                MoreRecords = kind == Kind.Disallow && DisallowNeverEnds,
                PagingCookie = "cookie",
            });
        }
    }

    private static Kind Classify(QueryExpression q)
    {
        var columns = q.ColumnSet.Columns;
        switch (q.EntityName)
        {
            case "sprk_invoice":
                return columns.Contains("sprk_vendororg") ? Kind.Header : Kind.Prior;
            case "sprk_matter":
            case "sprk_project":
            case "sprk_workassignment":
                return columns.Contains("statuscode") ? Kind.Core : Kind.Budget;
            case "sprk_billingevent":
                if (q.LinkEntities.Count == 0)
                {
                    return Kind.FeeLines;
                }

                return AllConditions(q).Any(c => c.AttributeName == "sprk_linestatus") ? Kind.Disallow : Kind.PreviousLines;
            case "sprk_timekeeper":
                return Kind.Roster;
            case "sprk_ratecardrate":
                return Kind.Rates;
            default:
                throw new InvalidOperationException($"Unexpected query on {q.EntityName}.");
        }
    }

    private static IEnumerable<ConditionExpression> Flatten(FilterExpression? filter) =>
        filter is null ? [] : filter.Conditions.Concat(filter.Filters.SelectMany(Flatten));

    private static IEnumerable<ConditionExpression> FlattenLink(LinkEntity link) =>
        Flatten(link.LinkCriteria).Concat(link.LinkEntities.SelectMany(FlattenLink));

    private static IEnumerable<ConditionExpression> AllConditions(QueryExpression q) =>
        Flatten(q.Criteria).Concat(q.LinkEntities.SelectMany(FlattenLink));

    private static bool Has(QueryExpression q, string attribute, ConditionOperator op, object value) =>
        AllConditions(q).Any(c => c.AttributeName == attribute && c.Operator == op && c.Values.Contains(value));

    // ── Rows ───────────────────────────────────────────────────────────────────────

    private static Entity InvoiceHeader(Guid? matter = null, Guid? project = null, Guid? workAssignment = null, Guid? firm = null, bool withFirm = true)
    {
        var e = new Entity("sprk_invoice", InvoiceId);
        if (withFirm)
        {
            e["sprk_vendororg"] = new EntityReference("sprk_organization", firm ?? FirmId);
        }

        if (matter is not null) e["sprk_matter"] = new EntityReference("sprk_matter", matter.Value);
        if (project is not null) e["sprk_project"] = new EntityReference("sprk_project", project.Value);
        if (workAssignment is not null) e["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", workAssignment.Value);
        return e;
    }

    private static Entity MatterCore()
    {
        var e = new Entity("sprk_matter", MatterId);
        e["sprk_mattername"] = "Acme v. Globex";
        e["sprk_matternumber"] = "MAT-001";
        e["statuscode"] = new OptionSetValue(659490002);
        e.FormattedValues["statuscode"] = "Working";
        e["sprk_openeddate"] = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        return e;
    }

    private static Entity MatterBudget(decimal? budget, decimal? spend, decimal? remaining, decimal? utilization, int? count)
    {
        var e = new Entity("sprk_matter", MatterId);
        if (budget is not null) e["sprk_totalbudget"] = new Money(budget.Value);
        if (spend is not null) e["sprk_totalspendtodate"] = new Money(spend.Value);
        if (remaining is not null) e["sprk_remainingbudget"] = new Money(remaining.Value);
        if (utilization is not null) e["sprk_budgetutilizationpercent"] = utilization.Value;
        if (count is not null) e["sprk_invoicecount"] = count.Value;
        e["modifiedon"] = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        return e;
    }

    private static Entity PriorInvoice(Guid id, string number, DateTime? date, decimal billed, decimal? approved, int statusCode)
    {
        var e = new Entity("sprk_invoice", id);
        e["sprk_invoicenumber"] = number;
        if (date is not null) e["sprk_invoicedate"] = date.Value;
        e["sprk_totalamount"] = new Money(billed);
        if (approved is not null) e["sprk_approvedtotal"] = new Money(approved.Value);
        e["statuscode"] = new OptionSetValue(statusCode);
        return e;
    }

    private static Entity DisallowLine(Guid owner, InvoiceLineStatus status, decimal billed, decimal? approved, string? reason)
    {
        var e = new Entity("sprk_billingevent", Guid.NewGuid());
        e["sprk_invoice"] = new EntityReference("sprk_invoice", owner);
        e["sprk_linestatus"] = new OptionSetValue((int)status);
        e["sprk_amount"] = new Money(billed);
        if (approved is not null) e["sprk_approvedamount"] = new Money(approved.Value);
        if (reason is not null) e["sprk_adjustmentreason"] = reason;
        return e;
    }

    private static Entity FeeLine(Guid? timekeeper, string asBilled, decimal rate, DateTime date)
    {
        var e = new Entity("sprk_billingevent", Guid.NewGuid());
        if (timekeeper is not null) e["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", timekeeper.Value);
        e["sprk_timekeeper"] = asBilled;
        e["sprk_rate"] = new Money(rate);
        e["sprk_eventdate"] = date;
        return e;
    }

    private static Entity PreviousLine(Guid timekeeper, decimal rate, DateTime date)
    {
        var e = new Entity("sprk_billingevent", Guid.NewGuid());
        e["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", timekeeper);
        e["sprk_rate"] = new Money(rate);
        e["sprk_eventdate"] = date;
        return e;
    }

    private static Entity Roster(Guid id, string name, int classification)
    {
        var e = new Entity("sprk_timekeeper", id);
        e["sprk_name"] = name;
        e["sprk_classification"] = new OptionSetValue(classification);
        return e;
    }

    private static Entity Rate(Guid? timekeeper, int? classification, decimal approved, decimal? rack, DateTime from, DateTime? to, Guid? cardMatter = null)
    {
        var e = new Entity("sprk_ratecardrate", Guid.NewGuid());
        if (timekeeper is not null) e["sprk_timekeeper"] = new EntityReference("sprk_timekeeper", timekeeper.Value);
        if (classification is not null) e["sprk_classification"] = new OptionSetValue(classification.Value);
        e["sprk_approvedrate"] = new Money(approved);
        if (rack is not null) e["sprk_rackrate"] = new Money(rack.Value);
        e["sprk_effectivefrom"] = from;
        if (to is not null) e["sprk_effectiveto"] = to.Value;
        if (cardMatter is not null)
        {
            e["card.sprk_matter"] = new AliasedValue("sprk_ratecard", "sprk_matter", new EntityReference("sprk_matter", cardMatter.Value));
        }

        return e;
    }

    private static Harness FullFixture()
    {
        var h = new Harness();

        h.Prior =
        [
            PriorInvoice(Guid.Parse("00000000-0000-0000-0000-00000000e001"), "INV-100.1", new DateTime(2026, 8, 1), 9000m, 8500m, InvoiceStatusCodes.Paid),
            PriorInvoice(Guid.Parse("00000000-0000-0000-0000-00000000e002"), "INV-099.1", new DateTime(2026, 7, 1), 7000m, null, InvoiceStatusCodes.ReturnedToFirm),
            PriorInvoice(Guid.Parse("00000000-0000-0000-0000-00000000e003"), "INV-098.1", null, 100m, null, 999),
        ];

        var ownerA = Guid.Parse("00000000-0000-0000-0000-00000000e001");
        var ownerB = Guid.Parse("00000000-0000-0000-0000-00000000e002");
        h.DisallowLines =
        [
            DisallowLine(ownerA, InvoiceLineStatus.Reduced, 1000m, 800m, "Block billing"),
            DisallowLine(ownerA, InvoiceLineStatus.Reduced, 500m, 500m, "  block   BILLING "),
            DisallowLine(ownerB, InvoiceLineStatus.Disallowed, 300m, null, "Travel time"),
            DisallowLine(ownerB, InvoiceLineStatus.Disallowed, 40m, null, null),
        ];

        h.FeeLines =
        [
            FeeLine(Alice, "A. Alice", 500m, new DateTime(2026, 3, 1)),
            FeeLine(Alice, "A. Alice", 500m, new DateTime(2026, 3, 2)),
            FeeLine(Bob, "B. Bob", 300m, new DateTime(2026, 3, 3)),
            FeeLine(null, "Zed  Z", 100m, new DateTime(2026, 3, 4)),
            FeeLine(null, "zed z", 120m, new DateTime(2026, 3, 5)),
        ];

        h.RosterRows = [Roster(Alice, "Alice Adams", Partner), Roster(Bob, "Bob Brown", Associate)];

        h.RateRows =
        [
            Rate(Alice, null, 450m, 600m, new DateTime(2026, 1, 1), null),
            Rate(Alice, null, 400m, null, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), cardMatter: MatterId),
            Rate(null, Partner, 480m, null, new DateTime(2024, 1, 1), null),
            Rate(null, Associate, 250m, null, new DateTime(2024, 1, 1), null),
        ];

        h.PreviousLines =
        [
            PreviousLine(Alice, 500m, new DateTime(2025, 12, 1)),
            PreviousLine(Alice, 450m, new DateTime(2026, 1, 15)),
            PreviousLine(Alice, 450m, new DateTime(2026, 2, 1)),
        ];

        return h;
    }

    // =========================================================================================
    // The five sections from fixtures (FR-33)
    // =========================================================================================

    [Fact]
    public async Task EverySection_IsBuiltFromFixtureData()
    {
        var h = FullFixture();

        var context = await h.Service().GetAsync(InvoiceId);

        context.Should().NotBeNull();
        context!.InvoiceId.Should().Be(InvoiceId);

        context.CoreRecord.Status.Should().Be(ContextSectionStatus.Available);
        context.CoreRecord.Data.Should().BeEquivalentTo(new CoreRecordContext(
            "matter", MatterId, "Acme v. Globex", "MAT-001", 659490002, "Working", new DateOnly(2026, 1, 5), null));

        context.Budget.Status.Should().Be(ContextSectionStatus.Available);
        context.Budget.Data.Should().BeEquivalentTo(new BudgetContext(
            25000m, 10000m, 15000m, 40m, 5, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)));

        var prior = context.PriorInvoices.Data!;
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        prior.Limit.Should().Be(InvoiceContextService.PriorInvoiceLimit);
        prior.HasMore.Should().BeFalse();
        prior.Items.Select(i => (i.Number, i.Status)).Should().Equal(
            ("INV-100.1", "Paid"), ("INV-099.1", "ReturnedToFirm"), ("INV-098.1", "Unknown"));
        prior.Items[0].Should().BeEquivalentTo(new PriorInvoiceItem(
            Guid.Parse("00000000-0000-0000-0000-00000000e001"), "INV-100.1", new DateOnly(2026, 8, 1), 9000m, 8500m, "Paid", InvoiceStatusCodes.Paid));
        prior.Items[2].InvoiceDate.Should().BeNull();

        var disallow = context.DisallowHistory.Data!;
        disallow.InvoicesConsidered.Should().Be(2);
        disallow.ReducedCount.Should().Be(2);
        disallow.ReducedAmount.Should().Be(200m, "billed minus approved: 1000-800 and 500-500");
        disallow.DisallowedCount.Should().Be(2);
        disallow.DisallowedAmount.Should().Be(340m);
        disallow.TopReasons.Should().Equal(new DisallowReason("Block billing", 2), new DisallowReason("Travel time", 1));

        var rates = context.RateHistory.Data!.Timekeepers;
        rates.Select(t => t.Name).Should().Equal("Alice Adams", "Bob Brown", "Zed Z");

        var alice = rates[0];
        alice.TimekeeperId.Should().Be(Alice);
        alice.ApprovedRates.Select(r => (r.Basis, r.Scope, r.ApprovedRate, r.EffectiveFrom, r.EffectiveTo)).Should().Equal(
            ("timekeeper", "firm", 450m, new DateOnly(2026, 1, 1), (DateOnly?)null),
            ("timekeeper", "coreRecord", 400m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            ("classification", "firm", 480m, new DateOnly(2024, 1, 1), (DateOnly?)null));
        alice.ApprovedRates[0].RackRate.Should().Be(600m);
        alice.BilledOnThisInvoice.Should().Equal(new BilledRateItem(500m, 2, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2)));
        alice.BilledPreviously.Select(b => (b.Rate, b.LineCount, b.LastSeen)).Should().Equal(
            (450m, 2, (DateOnly?)new DateOnly(2026, 2, 1)), (500m, 1, new DateOnly(2025, 12, 1)));

        // The classification rate of ANOTHER classification is not Alice's.
        rates[1].ApprovedRates.Select(r => (r.Basis, r.ApprovedRate)).Should().Equal(("classification", 250m));

        // A name the roster did not match: billed rates only, merged across spellings, no approved rates, no id.
        rates[2].TimekeeperId.Should().BeNull();
        rates[2].ApprovedRates.Should().BeEmpty();
        rates[2].BilledPreviously.Should().BeEmpty();
        rates[2].BilledOnThisInvoice.Select(b => b.Rate).Should().BeEquivalentTo(new[] { 100m, 120m });
    }

    [Fact]
    public async Task PriorInvoices_MoreThanTheLimit_ReturnsTheLimitAndSaysThereIsMore()
    {
        var h = FullFixture();
        h.Prior = Enumerable.Range(1, InvoiceContextService.PriorInvoiceLimit + 1)
            .Select(i => PriorInvoice(Guid.NewGuid(), $"INV-{i:000}", new DateTime(2026, 1, 1).AddDays(-i), 10m, null, InvoiceStatusCodes.InReview))
            .ToList();

        var prior = (await h.Service().GetAsync(InvoiceId))!.PriorInvoices.Data!;

        prior.Items.Should().HaveCount(InvoiceContextService.PriorInvoiceLimit);
        prior.HasMore.Should().BeTrue();
        prior.Items.Select(i => i.Number).Should().Equal(Enumerable.Range(1, InvoiceContextService.PriorInvoiceLimit).Select(i => $"INV-{i:000}"));
    }

    [Fact]
    public async Task PriorInvoices_AreAskedForMostRecentFirst_WithAStableTiebreak()
    {
        var h = FullFixture();

        await h.Service().GetAsync(InvoiceId);

        var q = h.Queries.Single(x => Classify(x) == Kind.Prior);
        q.Orders.Select(o => (o.AttributeName, o.OrderType)).Should().Equal(
            ("sprk_invoicedate", OrderType.Descending), ("createdon", OrderType.Descending), ("sprk_invoiceid", OrderType.Ascending));
        q.TopCount.Should().Be(InvoiceContextService.PriorInvoiceLimit + 1);
    }

    [Fact]
    public async Task ZeroHistory_IsAnEmptySection_NotAnUnavailableOne()
    {
        var h = new Harness();

        var context = (await h.Service().GetAsync(InvoiceId))!;

        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        context.PriorInvoices.Data!.Items.Should().BeEmpty();
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Available);
        context.DisallowHistory.Data.Should().BeEquivalentTo(new DisallowHistoryContext(0, 0, 0m, 0, 0m, []));
        context.RateHistory.Status.Should().Be(ContextSectionStatus.Available);
        context.RateHistory.Data!.Timekeepers.Should().BeEmpty();
        h.Count(Kind.Roster).Should().Be(0, "no roster-matched timekeeper, so no roster / rate / previous-line reads");
        h.Count(Kind.Rates).Should().Be(0);
        h.Count(Kind.PreviousLines).Should().Be(0);
    }

    [Fact]
    public async Task TopReasons_AreCappedAndOrderedByCountThenText()
    {
        var h = new Harness();
        var owner = Guid.NewGuid();
        h.DisallowLines =
        [
            .. Enumerable.Range(0, 3).Select(_ => DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "Zeta")),
            .. Enumerable.Range(0, 3).Select(_ => DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "Alpha")),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R1"),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R2"),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R3"),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R4"),
        ];

        var reasons = (await h.Service().GetAsync(InvoiceId))!.DisallowHistory.Data!.TopReasons;

        reasons.Should().HaveCount(InvoiceContextService.TopReasonLimit);
        reasons.Select(r => r.Reason).Should().Equal("Alpha", "Zeta", "R1", "R2", "R3");
    }

    // =========================================================================================
    // Scoping: the invoice's own firm and core record, never the current invoice, never a caller id
    // =========================================================================================

    [Fact]
    public async Task EveryCrossInvoiceQuery_IsScopedToTheInvoicesFirmAndCoreRecord_AndExcludesTheCurrentInvoice()
    {
        var h = FullFixture();

        await h.Service().GetAsync(InvoiceId);

        foreach (var kind in new[] { Kind.Prior, Kind.Disallow, Kind.PreviousLines })
        {
            var q = h.Queries.Single(x => Classify(x) == kind);
            Has(q, "sprk_vendororg", ConditionOperator.Equal, FirmId).Should().BeTrue($"{kind} is this firm's");
            Has(q, "sprk_matter", ConditionOperator.Equal, MatterId).Should().BeTrue($"{kind} is this core record's");
            Has(q, "sprk_invoiceid", ConditionOperator.NotEqual, InvoiceId).Should().BeTrue($"{kind} excludes the current invoice");
        }

        var rates = h.Queries.Single(x => Classify(x) == Kind.Rates);
        Has(rates, "sprk_firm", ConditionOperator.Equal, FirmId).Should().BeTrue("rate cards are this firm's");
        Has(rates, "statuscode", ConditionOperator.Equal, 659490000).Should().BeTrue("only Published cards");
        Has(rates, "sprk_matter", ConditionOperator.Equal, MatterId).Should().BeTrue("firm-wide cards plus cards scoped to this core record");

        Has(h.Queries.Single(x => Classify(x) == Kind.FeeLines), "sprk_invoice", ConditionOperator.Equal, InvoiceId).Should().BeTrue();

        // Every id any query mentions is the invoice's own firm / core record / invoice, or one of its timekeepers.
        var allowed = new HashSet<Guid> { InvoiceId, FirmId, MatterId, Alice, Bob };
        var mentioned = h.Queries.SelectMany(AllConditions).SelectMany(c => c.Values).OfType<Guid>();
        mentioned.Should().OnlyContain(id => allowed.Contains(id));
    }

    [Fact]
    public async Task Disallow_CountsOnlyDecidedInvoices_AndOnlyFeeAndExpenseLines()
    {
        var h = FullFixture();

        await h.Service().GetAsync(InvoiceId);

        var q = h.Queries.Single(x => Classify(x) == Kind.Disallow);
        var statuses = AllConditions(q).Single(c => c.AttributeName == "statuscode" && c.Operator == ConditionOperator.In).Values;
        statuses.Cast<int>().Should().BeEquivalentTo(new[] { InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid });
        AllConditions(q).Single(c => c.AttributeName == "sprk_linestatus").Values.Cast<int>()
            .Should().BeEquivalentTo(new[] { (int)InvoiceLineStatus.Reduced, (int)InvoiceLineStatus.Disallowed });
        AllConditions(q).Single(c => c.AttributeName == "sprk_costtype").Values.Cast<int>()
            .Should().BeEquivalentTo(new[] { (int)InvoiceLineCostType.Fee, (int)InvoiceLineCostType.Expense });
    }

    [Fact]
    public async Task ProjectInvoice_ScopesToTheProjectColumn()
    {
        var h = FullFixture();
        h.Header = InvoiceHeader(project: ProjectId);

        await h.Service().GetAsync(InvoiceId);

        var prior = h.Queries.Single(x => Classify(x) == Kind.Prior);
        Has(prior, "sprk_project", ConditionOperator.Equal, ProjectId).Should().BeTrue();
        Has(prior, "sprk_matter", ConditionOperator.Equal, MatterId).Should().BeFalse();
    }

    // =========================================================================================
    // Work Assignment: budget is not tracked (not an error)
    // =========================================================================================

    [Fact]
    public async Task WorkAssignmentInvoice_BudgetIsNotTracked_AndTheOtherSectionsStillLoad()
    {
        var h = FullFixture();
        h.Header = InvoiceHeader(workAssignment: WorkAssignmentId);
        h.CoreRow = new Entity("sprk_workassignment", WorkAssignmentId)
        {
            ["sprk_name"] = "WA-7",
            ["sprk_workassignmentnumber"] = "WA-0007",
            ["statuscode"] = new OptionSetValue(1),
        };
        h.CoreRow.FormattedValues["statuscode"] = "Active";

        var context = (await h.Service().GetAsync(InvoiceId))!;

        context.Budget.Status.Should().Be(ContextSectionStatus.NotTracked);
        context.Budget.Code.Should().Be(InvoiceContextService.CodeBudgetNotTracked);
        context.Budget.Message.Should().Contain("work assignments");
        context.Budget.Data.Should().BeNull();
        h.Queries.Where(q => Classify(q) == Kind.Budget).Should().BeEmpty("no budget read for a Work Assignment");

        context.CoreRecord.Data.Should().BeEquivalentTo(new CoreRecordContext("workAssignment", WorkAssignmentId, "WA-7", "WA-0007", 1, "Active", null, null));
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        Has(h.Queries.Single(x => Classify(x) == Kind.Prior), "sprk_regardingworkassignment", ConditionOperator.Equal, WorkAssignmentId).Should().BeTrue();
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Available);
        context.RateHistory.Status.Should().Be(ContextSectionStatus.Available);
    }

    // =========================================================================================
    // Budget: the rollups as stored, never computed here (task 106 escalation)
    // =========================================================================================

    [Fact]
    public async Task Budget_RollupsNotPopulated_IsUnavailable_AndNothingIsSummed()
    {
        var h = FullFixture();
        h.BudgetRow = MatterBudget(null, null, null, null, null);

        var context = (await h.Service().GetAsync(InvoiceId))!;

        context.Budget.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.Budget.Code.Should().Be(InvoiceContextService.CodeBudgetNotPopulated);
        context.Budget.Data.Should().BeNull();
        h.Queries.Where(q => q.EntityName is "sprk_invoice" or "sprk_budget")
            .Should().OnlyContain(q => new[] { Kind.Header, Kind.Prior }.Contains(Classify(q)), "spend is not recomputed from invoices or budgets");
    }

    [Fact]
    public async Task Budget_PartiallyPopulatedRollups_AreReturnedAsStored()
    {
        var h = FullFixture();
        h.BudgetRow = MatterBudget(55000m, 25000m, remaining: null, utilization: null, count: 2);

        var budget = (await h.Service().GetAsync(InvoiceId))!.Budget;

        budget.Status.Should().Be(ContextSectionStatus.Available);
        budget.Data!.Remaining.Should().BeNull("a missing rollup field is not filled in by arithmetic");
        budget.Data.UtilizationPercent.Should().BeNull();
        budget.Data.Budget.Should().Be(55000m);
        budget.Data.SpendToDate.Should().Be(25000m);
    }

    // =========================================================================================
    // One source failing leaves the others intact
    // =========================================================================================

    public static TheoryData<Kind, string> FailingSources => new()
    {
        { Kind.Core, InvoiceContextService.SectionCoreRecord },
        { Kind.Budget, InvoiceContextService.SectionBudget },
        { Kind.Prior, InvoiceContextService.SectionPriorInvoices },
        { Kind.Disallow, InvoiceContextService.SectionDisallowHistory },
        { Kind.FeeLines, InvoiceContextService.SectionRateHistory },
        { Kind.Roster, InvoiceContextService.SectionRateHistory },
        { Kind.Rates, InvoiceContextService.SectionRateHistory },
        { Kind.PreviousLines, InvoiceContextService.SectionRateHistory },
    };

    [Theory]
    [MemberData(nameof(FailingSources))]
    public async Task OneSourceThrowing_MakesOnlyThatSectionUnavailable(Kind failing, string section)
    {
        var h = FullFixture();
        h.Intercept = (kind, _, _) => kind == failing ? Task.FromException<EntityCollection>(new InvalidOperationException("Dataverse said no")) : null;

        var context = (await h.Service().GetAsync(InvoiceId))!;

        var sections = new (string Name, string Status, string? Code, string? Message)[]
        {
            (InvoiceContextService.SectionCoreRecord, context.CoreRecord.Status, context.CoreRecord.Code, context.CoreRecord.Message),
            (InvoiceContextService.SectionBudget, context.Budget.Status, context.Budget.Code, context.Budget.Message),
            (InvoiceContextService.SectionPriorInvoices, context.PriorInvoices.Status, context.PriorInvoices.Code, context.PriorInvoices.Message),
            (InvoiceContextService.SectionDisallowHistory, context.DisallowHistory.Status, context.DisallowHistory.Code, context.DisallowHistory.Message),
            (InvoiceContextService.SectionRateHistory, context.RateHistory.Status, context.RateHistory.Code, context.RateHistory.Message),
        };

        foreach (var s in sections)
        {
            if (s.Name == section)
            {
                s.Status.Should().Be(ContextSectionStatus.Unavailable, s.Name);
                s.Code.Should().Be(s.Name + ".unavailable");
                s.Message.Should().NotBeNullOrWhiteSpace().And.NotContain("Dataverse said no", "the cause is logged, not returned");
            }
            else
            {
                s.Status.Should().Be(ContextSectionStatus.Available, s.Name + " is independent of the failing source");
            }
        }
    }

    [Fact]
    public async Task ASlowSource_TimesOutThatSectionOnly()
    {
        var h = FullFixture();
        h.Intercept = (kind, _, ct) => kind == Kind.Prior ? WaitForever(ct) : null;

        var context = (await h.Service(deadline: TimeSpan.FromMilliseconds(300)).GetAsync(InvoiceId))!;

        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.PriorInvoices.Code.Should().Be(InvoiceContextService.SectionPriorInvoices + ".timeout");
        context.CoreRecord.Status.Should().Be(ContextSectionStatus.Available);
        context.Budget.Status.Should().Be(ContextSectionStatus.Available);
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Available);
        context.RateHistory.Status.Should().Be(ContextSectionStatus.Available);
    }

    [Fact]
    public async Task ACancelledRequest_Propagates_NotAnUnavailableSection()
    {
        var h = FullFixture();
        using var cts = new CancellationTokenSource();
        h.Intercept = (kind, _, ct) =>
        {
            if (kind == Kind.Prior)
            {
                cts.Cancel();
                return WaitForever(ct);
            }

            return null;
        };

        var act = () => h.Service().GetAsync(InvoiceId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HeaderThrowing_MakesEverySectionUnavailable_WithAStableCode()
    {
        var h = FullFixture();
        h.Intercept = (kind, _, _) => kind == Kind.Header ? Task.FromException<EntityCollection>(new InvalidOperationException("boom")) : null;

        var context = (await h.Service().GetAsync(InvoiceId))!;

        new[] { context.CoreRecord.Code, context.Budget.Code, context.PriorInvoices.Code, context.DisallowHistory.Code, context.RateHistory.Code }
            .Should().OnlyContain(c => c == InvoiceContextService.CodeInvoiceUnavailable);
        h.Queries.Should().ContainSingle("nothing else is read without the invoice's firm and core record");
    }

    [Fact]
    public async Task UnknownInvoice_ReturnsNull()
    {
        var h = new Harness();
        h.Intercept = (kind, _, _) => kind == Kind.Header ? Task.FromResult(new EntityCollection()) : null;

        (await h.Service().GetAsync(InvoiceId)).Should().BeNull();
        h.Queries.Should().ContainSingle();
    }

    // =========================================================================================
    // Unresolvable scope fails closed instead of widening
    // =========================================================================================

    [Fact]
    public async Task InvoiceWithNoFirm_HistorySectionsSayWhy_AndNoCrossInvoiceQueryRuns()
    {
        var h = FullFixture();
        h.Header = InvoiceHeader(matter: MatterId, withFirm: false);

        var context = (await h.Service().GetAsync(InvoiceId))!;

        foreach (var code in new[] { context.PriorInvoices.Code, context.DisallowHistory.Code, context.RateHistory.Code })
        {
            code.Should().Be(InvoiceContextService.CodeFirmUnresolved);
        }

        context.CoreRecord.Status.Should().Be(ContextSectionStatus.Available);
        context.Budget.Status.Should().Be(ContextSectionStatus.Available);
        h.Queries.Select(Classify).Should().BeEquivalentTo(new[] { Kind.Header, Kind.Core, Kind.Budget },
            "a null firm must never become a filter that matches every invoice without a firm");
    }

    [Theory]
    [InlineData(false, InvoiceContextService.CodeCoreUnresolved)]
    [InlineData(true, InvoiceContextService.CodeCoreAmbiguous)]
    public async Task InvoiceWithNoCoreRecord_OrTwo_EverySectionSaysWhy_AndNothingIsQueried(bool both, string code)
    {
        var h = FullFixture();
        h.Header = both ? InvoiceHeader(matter: MatterId, project: ProjectId) : InvoiceHeader();

        var context = (await h.Service().GetAsync(InvoiceId))!;

        new[] { context.CoreRecord.Code, context.Budget.Code, context.PriorInvoices.Code, context.DisallowHistory.Code, context.RateHistory.Code }
            .Should().OnlyContain(c => c == code);
        h.Queries.Should().ContainSingle();
    }

    // =========================================================================================
    // Bounded reads, determinism
    // =========================================================================================

    [Fact]
    public async Task QueryCount_DoesNotDependOnTheInvoicesLineCount()
    {
        var small = FullFixture();
        var large = FullFixture();
        large.FeeLines = Enumerable.Range(0, 400)
            .Select(i => FeeLine(i % 2 == 0 ? Alice : Bob, "x", 500m + (i % 5), new DateTime(2026, 3, 1).AddDays(i % 20)))
            .ToList();

        await small.Service().GetAsync(InvoiceId);
        await large.Service().GetAsync(InvoiceId);

        large.Queries.Count.Should().Be(small.Queries.Count);
        small.Queries.Count.Should().Be(9, "header + core + budget + prior + disallow + fee lines + roster + rates + previous lines");
        large.Queries.Select(Classify).Should().BeEquivalentTo(small.Queries.Select(Classify));
    }

    [Fact]
    public async Task AHistoryTooLargeToPage_IsReportedNotSummarisedFromAPartialRead()
    {
        var h = FullFixture();
        h.DisallowNeverEnds = true;

        var context = (await h.Service().GetAsync(InvoiceId))!;

        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.DisallowHistory.Code.Should().Be(InvoiceContextService.CodeDisallowTooLarge);
        h.Count(Kind.Disallow).Should().Be(InvoiceContextService.MaxPages);
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
    }

    [Fact]
    public async Task SameData_GivesTheSameResponse()
    {
        var first = JsonSerializer.Serialize(await FullFixture().Service().GetAsync(InvoiceId));
        var second = JsonSerializer.Serialize(await FullFixture().Service().GetAsync(InvoiceId));

        second.Should().Be(first);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Block \t billing\r\n", "Block billing")]
    public void NormalizeReason_TrimsAndCollapsesWhitespace(string? input, string? expected) =>
        InvoiceContextService.NormalizeReason(input).Should().Be(expected);

    private static async Task<EntityCollection> WaitForever(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new EntityCollection();
    }
}
