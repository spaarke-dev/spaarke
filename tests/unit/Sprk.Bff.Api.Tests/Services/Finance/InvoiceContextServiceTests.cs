using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
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

    private static readonly InvoiceContextService.InvoiceCaller Caller = new("caller-oid", "caller-token");

    private static readonly Guid SiblingA = Guid.Parse("00000000-0000-0000-0000-00000000e001");
    private static readonly Guid SiblingB = Guid.Parse("00000000-0000-0000-0000-00000000e002");
    private static readonly Guid SiblingC = Guid.Parse("00000000-0000-0000-0000-00000000e003");

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

        /// <summary>What the caller may do to each sibling invoice; by default Read on every sibling.</summary>
        public SiblingAccess Access { get; } = new();

        public Harness()
        {
            Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken ct) => Answer(query, ct));
        }

        public InvoiceContextService Service(TimeSpan? deadline = null)
        {
            var authorization = new AuthorizationService(
                Access, [new OperationAccessRule(NullLogger<OperationAccessRule>.Instance)], NullLogger<AuthorizationService>.Instance);
            var service = new InvoiceContextService(Entities.Object, authorization, NullLogger<InvoiceContextService>.Instance);
            if (deadline is { } d)
            {
                service.Deadline = d;
            }

            return service;
        }

        public int Count(Kind kind) => Queries.Count(q => Classify(q) == kind);

        /// <summary>Dataverse would return only the lines of the invoices the query names; the fake does the same.</summary>
        private static List<Entity> OwnedBy(List<Entity> lines, QueryExpression query)
        {
            var named = AllConditions(query)
                .Single(c => c.AttributeName == "sprk_invoice" && c.Operator == ConditionOperator.In).Values.Cast<Guid>().ToHashSet();
            return lines.Where(l => named.Contains(l.GetAttributeValue<EntityReference>("sprk_invoice").Id)).ToList();
        }

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
                Kind.Disallow => OwnedBy(DisallowLines, query),
                Kind.FeeLines => FeeLines,
                Kind.Roster => RosterRows,
                Kind.Rates => RateRows,
                Kind.PreviousLines => OwnedBy(PreviousLines, query),
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
                if (AllConditions(q).Any(c => c.AttributeName == "sprk_invoice" && c.Operator == ConditionOperator.Equal))
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

    private static Entity PreviousLine(Guid timekeeper, decimal rate, DateTime date, Guid? owner = null)
    {
        var e = new Entity("sprk_billingevent", Guid.NewGuid());
        e["sprk_invoice"] = new EntityReference("sprk_invoice", owner ?? SiblingA);
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
            PriorInvoice(SiblingA, "INV-100.1", new DateTime(2026, 8, 1), 9000m, 8500m, InvoiceStatusCodes.Paid),
            PriorInvoice(SiblingB, "INV-099.1", new DateTime(2026, 7, 1), 7000m, null, InvoiceStatusCodes.Approved),
            PriorInvoice(SiblingC, "INV-098.1", null, 100m, null, 999),
        ];

        var ownerA = SiblingA;
        var ownerB = SiblingB;
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

        var context = await h.Service().GetAsync(InvoiceId, Caller);

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
            ("INV-100.1", "Paid"), ("INV-099.1", "Approved"), ("INV-098.1", "Unknown"));
        prior.Items[0].Should().BeEquivalentTo(new PriorInvoiceItem(
            SiblingA, "INV-100.1", new DateOnly(2026, 8, 1), 9000m, 8500m, "Paid", InvoiceStatusCodes.Paid));
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

        var prior = (await h.Service().GetAsync(InvoiceId, Caller))!.PriorInvoices.Data!;

        prior.Items.Should().HaveCount(InvoiceContextService.PriorInvoiceLimit);
        prior.HasMore.Should().BeTrue();
        prior.Items.Select(i => i.Number).Should().Equal(Enumerable.Range(1, InvoiceContextService.PriorInvoiceLimit).Select(i => $"INV-{i:000}"));
    }

    [Fact]
    public async Task PriorInvoices_AreAskedForMostRecentFirst_WithAStableTiebreak()
    {
        var h = FullFixture();

        await h.Service().GetAsync(InvoiceId, Caller);

        var q = h.Queries.Single(x => Classify(x) == Kind.Prior);
        q.Orders.Select(o => (o.AttributeName, o.OrderType)).Should().Equal(
            ("sprk_invoicedate", OrderType.Descending), ("createdon", OrderType.Descending), ("sprk_invoiceid", OrderType.Ascending));
        q.TopCount.Should().BeNull("every sibling is read, so the readable ones can be chosen from before the limit is applied");
    }

    [Fact]
    public async Task ZeroHistory_IsAnEmptySection_NotAnUnavailableOne()
    {
        var h = new Harness();

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

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
        h.Prior = [PriorInvoice(owner, "INV-1", new DateTime(2026, 1, 1), 10m, null, InvoiceStatusCodes.Paid)];
        h.DisallowLines =
        [
            .. Enumerable.Range(0, 3).Select(_ => DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "Zeta")),
            .. Enumerable.Range(0, 3).Select(_ => DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "Alpha")),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R1"),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R2"),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R3"),
            DisallowLine(owner, InvoiceLineStatus.Disallowed, 1m, null, "R4"),
        ];

        var reasons = (await h.Service().GetAsync(InvoiceId, Caller))!.DisallowHistory.Data!.TopReasons;

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

        await h.Service().GetAsync(InvoiceId, Caller);

        // The siblings are read once, scoped to this firm and core record, never the current invoice ...
        var q = h.Queries.Single(x => Classify(x) == Kind.Prior);
        Has(q, "sprk_vendororg", ConditionOperator.Equal, FirmId).Should().BeTrue("siblings are this firm's");
        Has(q, "sprk_matter", ConditionOperator.Equal, MatterId).Should().BeTrue("siblings are this core record's");
        Has(q, "sprk_invoiceid", ConditionOperator.NotEqual, InvoiceId).Should().BeTrue("siblings exclude the current invoice");

        // ... and the line reads that draw on other invoices name only those siblings (which never include the current invoice).
        foreach (var kind in new[] { Kind.Disallow, Kind.PreviousLines })
        {
            var named = AllConditions(h.Queries.Single(x => Classify(x) == kind))
                .Single(c => c.AttributeName == "sprk_invoice").Values.Cast<Guid>().ToList();
            named.Should().NotBeEmpty().And.NotContain(InvoiceId).And.OnlyContain(id => new[] { SiblingA, SiblingB, SiblingC }.Contains(id), kind.ToString());
        }

        var rates = h.Queries.Single(x => Classify(x) == Kind.Rates);
        Has(rates, "sprk_firm", ConditionOperator.Equal, FirmId).Should().BeTrue("rate cards are this firm's");
        Has(rates, "statuscode", ConditionOperator.Equal, 659490000).Should().BeTrue("only Published cards");
        Has(rates, "sprk_matter", ConditionOperator.Equal, MatterId).Should().BeTrue("firm-wide cards plus cards scoped to this core record");

        Has(h.Queries.Single(x => Classify(x) == Kind.FeeLines), "sprk_invoice", ConditionOperator.Equal, InvoiceId).Should().BeTrue();

        // Every id any query mentions is the invoice's own firm / core record / invoice, or one of its timekeepers.
        var allowed = new HashSet<Guid> { InvoiceId, FirmId, MatterId, Alice, Bob, SiblingA, SiblingB, SiblingC };
        var mentioned = h.Queries.SelectMany(AllConditions).SelectMany(c => c.Values).OfType<Guid>();
        mentioned.Should().OnlyContain(id => allowed.Contains(id));
    }

    [Fact]
    public async Task Disallow_CountsOnlyDecidedInvoices_AndOnlyFeeAndExpenseLines()
    {
        var h = FullFixture();
        var returned = Guid.NewGuid();
        h.Prior.Add(PriorInvoice(returned, "INV-RET", new DateTime(2026, 6, 1), 10m, null, InvoiceStatusCodes.ReturnedToFirm));
        h.DisallowLines.Add(DisallowLine(returned, InvoiceLineStatus.Disallowed, 5000m, null, "Not decided"));

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        var q = h.Queries.Single(x => Classify(x) == Kind.Disallow);
        AllConditions(q).Single(c => c.AttributeName == "sprk_invoice").Values.Cast<Guid>()
            .Should().BeEquivalentTo(new[] { SiblingA, SiblingB }, "only Approved / Sent to AP / Paid siblings; not the Returned one (nor the unknown-status one)");
        context.DisallowHistory.Data!.DisallowedAmount.Should().Be(340m);
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

        await h.Service().GetAsync(InvoiceId, Caller);

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

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

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

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

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

        var budget = (await h.Service().GetAsync(InvoiceId, Caller))!.Budget;

        budget.Status.Should().Be(ContextSectionStatus.Available);
        budget.Data!.Remaining.Should().BeNull("a missing rollup field is not filled in by arithmetic");
        budget.Data.UtilizationPercent.Should().BeNull();
        budget.Data.Budget.Should().Be(55000m);
        budget.Data.SpendToDate.Should().Be(25000m);
    }

    // =========================================================================================
    // One source failing leaves the others intact
    // =========================================================================================

    // The sibling invoice read is shared by the three sections that draw on other invoices, so when it fails they all say so.
    public static TheoryData<Kind, string> FailingSources => new()
    {
        { Kind.Core, InvoiceContextService.SectionCoreRecord },
        { Kind.Budget, InvoiceContextService.SectionBudget },
        { Kind.Prior, string.Join('|', InvoiceContextService.SectionPriorInvoices, InvoiceContextService.SectionDisallowHistory, InvoiceContextService.SectionRateHistory) },
        { Kind.Disallow, InvoiceContextService.SectionDisallowHistory },
        { Kind.FeeLines, InvoiceContextService.SectionRateHistory },
        { Kind.Roster, InvoiceContextService.SectionRateHistory },
        { Kind.Rates, InvoiceContextService.SectionRateHistory },
        { Kind.PreviousLines, InvoiceContextService.SectionRateHistory },
    };

    [Theory]
    [MemberData(nameof(FailingSources))]
    public async Task OneSourceThrowing_MakesOnlyThatSectionUnavailable(Kind failing, string failingSections)
    {
        var h = FullFixture();
        h.Intercept = (kind, _, _) => kind == failing ? Task.FromException<EntityCollection>(new InvalidOperationException("Dataverse said no")) : null;

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

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
            if (failingSections.Split('|').Contains(s.Name))
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
        h.Intercept = (kind, _, ct) => kind == Kind.Disallow ? WaitForever(ct) : null;

        var context = (await h.Service(deadline: TimeSpan.FromMilliseconds(300)).GetAsync(InvoiceId, Caller))!;

        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.DisallowHistory.Code.Should().Be(InvoiceContextService.SectionDisallowHistory + ".timeout");
        context.CoreRecord.Status.Should().Be(ContextSectionStatus.Available);
        context.Budget.Status.Should().Be(ContextSectionStatus.Available);
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
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

        var act = () => h.Service().GetAsync(InvoiceId, Caller, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HeaderThrowing_MakesEverySectionUnavailable_WithAStableCode()
    {
        var h = FullFixture();
        h.Intercept = (kind, _, _) => kind == Kind.Header ? Task.FromException<EntityCollection>(new InvalidOperationException("boom")) : null;

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        new[] { context.CoreRecord.Code, context.Budget.Code, context.PriorInvoices.Code, context.DisallowHistory.Code, context.RateHistory.Code }
            .Should().OnlyContain(c => c == InvoiceContextService.CodeInvoiceUnavailable);
        h.Queries.Should().ContainSingle("nothing else is read without the invoice's firm and core record");
    }

    [Fact]
    public async Task UnknownInvoice_ReturnsNull()
    {
        var h = new Harness();
        h.Intercept = (kind, _, _) => kind == Kind.Header ? Task.FromResult(new EntityCollection()) : null;

        (await h.Service().GetAsync(InvoiceId, Caller)).Should().BeNull();
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

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

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

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        new[] { context.CoreRecord.Code, context.Budget.Code, context.PriorInvoices.Code, context.DisallowHistory.Code, context.RateHistory.Code }
            .Should().OnlyContain(c => c == code);
        h.Queries.Should().ContainSingle();
    }

    // =========================================================================================
    // Other invoices are limited to the ones the CALLER can Read (fix-round-2)
    // =========================================================================================

    [Fact]
    public async Task AnUnreadableSibling_IsExcludedFromPriorInvoices_DisallowHistory_AndPreviousRates()
    {
        var h = FullFixture();
        h.Access.Refuse(SiblingB);   // B ("INV-099.1", Approved, with disallowed lines) is not the caller's to see
        h.PreviousLines.Add(PreviousLine(Alice, 777m, new DateTime(2026, 5, 1), owner: SiblingB));

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        // priorInvoices: B's number, dates, totals, status and id are absent.
        context.PriorInvoices.Data!.Items.Select(i => i.Number).Should().Equal("INV-100.1", "INV-098.1");

        // disallowHistory: recomputed over A only (B's 300 + 40 disallowed and its "Travel time" reason are gone).
        var disallow = context.DisallowHistory.Data!;
        disallow.InvoicesConsidered.Should().Be(1);
        disallow.ReducedCount.Should().Be(2);
        disallow.ReducedAmount.Should().Be(200m);
        disallow.DisallowedCount.Should().Be(0);
        disallow.DisallowedAmount.Should().Be(0m);
        disallow.TopReasons.Should().Equal(new DisallowReason("Block billing", 2));

        // rateHistory: the rate billed on B (777) is not shown.
        context.RateHistory.Data!.Timekeepers[0].BilledPreviously.Select(b => b.Rate).Should().NotContain(777m);

        // Nothing about B reaches the response or the line queries.
        var json = JsonSerializer.Serialize(context);
        json.Should().NotContain("INV-099.1").And.NotContain(SiblingB.ToString());
        h.Queries.Where(q => Classify(q) is Kind.Disallow or Kind.PreviousLines)
            .SelectMany(AllConditions).SelectMany(c => c.Values).OfType<Guid>()
            .Should().NotContain(SiblingB, "an unreadable invoice's lines are never even requested");
    }

    [Fact]
    public async Task EverySiblingIsChecked_AsTheCaller_OnTheInvoiceEntitySet()
    {
        var h = FullFixture();

        await h.Service().GetAsync(InvoiceId, Caller);

        h.Access.Calls.Where(c => c.EntitySet == "sprk_invoices").Should()
            .BeEquivalentTo(new[] { SiblingA, SiblingB, SiblingC }.Select(id => ("caller-oid", "sprk_invoices", id, (string?)"caller-token")));
    }

    [Fact]
    public async Task WhenNoSiblingIsReadable_TheSectionsAreEmpty_NotUnavailable_AndNoLineQueryRuns()
    {
        var h = FullFixture();
        h.Access.Refuse(SiblingA, SiblingB, SiblingC);

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        context.PriorInvoices.Data!.Items.Should().BeEmpty();
        context.DisallowHistory.Data.Should().BeEquivalentTo(new DisallowHistoryContext(0, 0, 0m, 0, 0m, []));
        context.RateHistory.Data!.Timekeepers.Should().NotBeEmpty("the invoice's own lines are still shown");
        context.RateHistory.Data.Timekeepers.SelectMany(t => t.BilledPreviously).Should().BeEmpty();
        h.Count(Kind.Disallow).Should().Be(0);
        h.Count(Kind.PreviousLines).Should().Be(0);
    }

    public enum AccessFailure { LookupFaulted, LookupThrows }

    [Theory]
    [InlineData(AccessFailure.LookupFaulted)]
    [InlineData(AccessFailure.LookupThrows)]
    public async Task AFailedAccessCheck_MakesTheOtherInvoiceSectionsUnavailable_NeverTheUnfilteredData(AccessFailure failure)
    {
        var h = FullFixture();
        if (failure == AccessFailure.LookupFaulted)
        {
            h.Access.FaultFor(SiblingB);
        }
        else
        {
            h.Access.ThrowFor(SiblingB);
        }

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.PriorInvoices.Code.Should().Be(InvoiceContextService.CodePriorAccessCheckFailed);
        context.PriorInvoices.Data.Should().BeNull();
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.DisallowHistory.Code.Should().Be(InvoiceContextService.CodeDisallowAccessCheckFailed);
        context.DisallowHistory.Data.Should().BeNull();
        context.RateHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.RateHistory.Code.Should().Be(InvoiceContextService.CodeRateAccessCheckFailed);
        context.RateHistory.Data.Should().BeNull();

        // The sections that do not depend on other invoices are unaffected; no sibling's lines were requested.
        context.CoreRecord.Status.Should().Be(ContextSectionStatus.Available);
        context.Budget.Status.Should().Be(ContextSectionStatus.Available);
        h.Count(Kind.Disallow).Should().Be(0);
        h.Count(Kind.PreviousLines).Should().Be(0);
        JsonSerializer.Serialize(context).Should().NotContain("INV-");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task ACallerWithoutAToken_GetsNoOtherInvoices_AndNothingIsFetchedForThem(string? token)
    {
        var h = FullFixture();

        var context = (await h.Service().GetAsync(InvoiceId, new InvoiceContextService.InvoiceCaller("caller-oid", token)))!;

        context.PriorInvoices.Code.Should().Be(InvoiceContextService.CodePriorAccessCheckFailed);
        context.DisallowHistory.Code.Should().Be(InvoiceContextService.CodeDisallowAccessCheckFailed);
        context.RateHistory.Code.Should().Be(InvoiceContextService.CodeRateAccessCheckFailed);
        context.CoreRecord.Code.Should().Be(InvoiceContextService.CodeCoreAccessCheckFailed, "the core record needs the caller's rights too");
        context.Budget.Code.Should().Be(InvoiceContextService.CodeBudgetAccessCheckFailed);
        h.Access.Calls.Should().BeEmpty();
        h.Count(Kind.Disallow).Should().Be(0);
        h.Count(Kind.PreviousLines).Should().Be(0);
    }

    [Fact]
    public async Task MoreReadableSiblingsThanCanBeSummarised_PriorInvoicesSaysMore_TheAggregatesAreUnavailable()
    {
        var h = FullFixture();
        h.Prior = Enumerable.Range(1, InvoiceContextService.MaxReadableSiblings + 1)
            .Select(i => PriorInvoice(Guid.NewGuid(), $"INV-{i:000}", new DateTime(2026, 1, 1).AddDays(-i), 10m, null, InvoiceStatusCodes.Paid))
            .ToList();

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        h.Access.Calls.Count(c => c.EntitySet == "sprk_invoices").Should().Be(InvoiceContextService.MaxReadableSiblings + 1);
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        context.PriorInvoices.Data!.HasMore.Should().BeTrue();
        context.DisallowHistory.Code.Should().Be(InvoiceContextService.CodeDisallowTooLarge, "a total over some of the readable invoices would be wrong, so it is not given");
        context.RateHistory.Code.Should().Be(InvoiceContextService.CodeRateTooLarge);
    }

    [Fact]
    public async Task SiblingsTheCallerCannotRead_NeverMakeTheAnswerTooLarge_OrSayMore()
    {
        // 250 other invoices on the record, 3 of them readable: nothing in the answer may depend on the 247 the caller cannot open.
        var h = FullFixture();
        h.Prior = Enumerable.Range(1, 250)
            .Select(i => PriorInvoice(Guid.NewGuid(), $"INV-{i:000}", new DateTime(2026, 1, 1).AddDays(-i), 10m, null, InvoiceStatusCodes.Paid))
            .ToList();
        h.Access.Refuse(h.Prior.Skip(3).Select(e => e.Id).ToArray());

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        context.PriorInvoices.Data!.Items.Should().HaveCount(3);
        context.PriorInvoices.Data.HasMore.Should().BeFalse("only invoices the caller can read may say there are more");
        context.DisallowHistory.Code.Should().NotBe(InvoiceContextService.CodeDisallowTooLarge);
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Available);
        context.RateHistory.Code.Should().NotBe(InvoiceContextService.CodeRateTooLarge);
    }

    /// <summary>The caller's rights on each sibling invoice, answered the way Dataverse's RetrievePrincipalAccess would.</summary>
    private sealed class SiblingAccess : IAccessDataSource
    {
        private readonly HashSet<Guid> _refused = [];
        private readonly HashSet<Guid> _faulted = [];
        private readonly HashSet<Guid> _throws = [];

        public List<(string UserId, string EntitySet, Guid Id, string? Token)> Calls { get; } = [];

        public void Refuse(params Guid[] ids) => _refused.UnionWith(ids);

        public void FaultFor(Guid id) => _faulted.Add(id);

        public void ThrowFor(Guid id) => _throws.Add(id);

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add((userId, entitySetName, recordId, userAccessToken));
            }

            if (_throws.Contains(recordId))
            {
                return Task.FromException<AccessSnapshot>(new InvalidOperationException("Dataverse unreachable"));
            }

            var readable = !_faulted.Contains(recordId) && !_refused.Contains(recordId);
            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = readable ? AccessRights.Read : AccessRights.None,
                Faulted = _faulted.Contains(recordId),
            });
        }

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("The panel asks about invoices, not documents.");
    }

    // =========================================================================================
    // The core record is the caller's to read too (fix-round-3)
    // =========================================================================================

    [Fact]
    public async Task ACoreRecordTheCallerCannotRead_GivesNoDetailsNoBudgetAndNoCoreScopedRates()
    {
        var h = FullFixture();
        h.Access.Refuse(MatterId);   // Read on the invoice (the route's check), none on its matter

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.CoreRecord.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.CoreRecord.Code.Should().Be(InvoiceContextService.CodeCoreAccessDenied);
        context.CoreRecord.Data.Should().BeNull();
        context.Budget.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.Budget.Code.Should().Be(InvoiceContextService.CodeBudgetAccessDenied);
        context.Budget.Data.Should().BeNull();
        context.RateHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.RateHistory.Code.Should().Be(InvoiceContextService.CodeRateAccessDenied);
        context.RateHistory.Data.Should().BeNull();

        // Nothing of the matter was even read: no core row, no rollups, no rate-card read.
        h.Count(Kind.Core).Should().Be(0);
        h.Count(Kind.Budget).Should().Be(0);
        h.Count(Kind.Rates).Should().Be(0);
        var json = JsonSerializer.Serialize(context);
        json.Should().NotContain("Acme v. Globex").And.NotContain("MAT-001").And.NotContain("10000");

        // The sections about OTHER INVOICES depend on the invoices' rights, not the matter's.
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Available);
    }

    [Theory]
    [InlineData(AccessFailure.LookupFaulted)]
    [InlineData(AccessFailure.LookupThrows)]
    public async Task AFailedCoreRecordAccessCheck_MakesTheCoreSectionsUnavailable_NeverTheData(AccessFailure failure)
    {
        var h = FullFixture();
        if (failure == AccessFailure.LookupFaulted)
        {
            h.Access.FaultFor(MatterId);
        }
        else
        {
            h.Access.ThrowFor(MatterId);
        }

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.CoreRecord.Code.Should().Be(InvoiceContextService.CodeCoreAccessCheckFailed);
        context.Budget.Code.Should().Be(InvoiceContextService.CodeBudgetAccessCheckFailed);
        context.RateHistory.Code.Should().Be(InvoiceContextService.CodeRateAccessCheckFailed);
        new[] { context.CoreRecord.Status, context.Budget.Status, context.RateHistory.Status }
            .Should().OnlyContain(st => st == ContextSectionStatus.Unavailable);
        context.CoreRecord.Data.Should().BeNull();
        context.Budget.Data.Should().BeNull();
        context.RateHistory.Data.Should().BeNull();
        h.Count(Kind.Core).Should().Be(0);
        h.Count(Kind.Budget).Should().Be(0);
        h.Count(Kind.Rates).Should().Be(0);
    }

    [Theory]
    [InlineData(CoreKindCase.Matter, "sprk_matters")]
    [InlineData(CoreKindCase.Project, "sprk_projects")]
    [InlineData(CoreKindCase.WorkAssignment, "sprk_workassignments")]
    public async Task TheCoreRecordIsCheckedOnItsOwnEntitySet_AsTheCaller(CoreKindCase kind, string entitySet)
    {
        var h = FullFixture();
        Guid id;
        switch (kind)
        {
            case CoreKindCase.Matter:
                id = MatterId;
                break;
            case CoreKindCase.Project:
                id = ProjectId;
                h.Header = InvoiceHeader(project: ProjectId);
                break;
            default:
                id = WorkAssignmentId;
                h.Header = InvoiceHeader(workAssignment: WorkAssignmentId);
                h.CoreRow = new Entity("sprk_workassignment", WorkAssignmentId) { ["sprk_name"] = "WA", ["statuscode"] = new OptionSetValue(1) };
                break;
        }

        await h.Service().GetAsync(InvoiceId, Caller);

        h.Access.Calls.Should().Contain(("caller-oid", entitySet, id, (string?)"caller-token"));
        h.Access.Calls.Count(c => c.EntitySet != "sprk_invoices").Should().Be(1, "one check, shared by the three sections that need it");
    }

    public enum CoreKindCase { Matter, Project, WorkAssignment }

    [Fact]
    public async Task AWorkAssignmentsBudget_IsStillNotTracked_WhateverTheCallersRights()
    {
        var h = FullFixture();
        h.Header = InvoiceHeader(workAssignment: WorkAssignmentId);
        h.CoreRow = new Entity("sprk_workassignment", WorkAssignmentId) { ["sprk_name"] = "WA", ["statuscode"] = new OptionSetValue(1) };
        h.Access.Refuse(WorkAssignmentId);

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.Budget.Status.Should().Be(ContextSectionStatus.NotTracked, "nothing is read for it, so there is nothing to withhold");
        context.CoreRecord.Code.Should().Be(InvoiceContextService.CodeCoreAccessDenied);
    }

    // =========================================================================================
    // InvoicesConsidered counts the invoices examined (fix-round-3)
    // =========================================================================================

    [Fact]
    public async Task InvoicesConsidered_CountsEveryDecidedReadableInvoice_NotOnlyThoseWithCuts()
    {
        var h = new Harness();
        var decided = Enumerable.Range(1, 12).Select(_ => Guid.NewGuid()).ToList();
        h.Prior = decided.Select((id, i) => PriorInvoice(id, $"INV-{i:00}", new DateTime(2026, 1, 1).AddDays(-i), 10m, null, InvoiceStatusCodes.Paid)).ToList();
        h.Prior.Add(PriorInvoice(Guid.NewGuid(), "INV-RET", new DateTime(2025, 1, 1), 10m, null, InvoiceStatusCodes.ReturnedToFirm));   // not decided
        h.DisallowLines =
        [
            DisallowLine(decided[0], InvoiceLineStatus.Disallowed, 10m, null, "x"),
            DisallowLine(decided[1], InvoiceLineStatus.Reduced, 10m, 5m, "y"),
        ];

        var disallow = (await h.Service().GetAsync(InvoiceId, Caller))!.DisallowHistory.Data!;

        disallow.InvoicesConsidered.Should().Be(12, "12 decided invoices were examined, 2 of them had cuts");
        disallow.DisallowedCount.Should().Be(1);
        disallow.ReducedCount.Should().Be(1);
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

        await small.Service().GetAsync(InvoiceId, Caller);
        await large.Service().GetAsync(InvoiceId, Caller);

        large.Queries.Count.Should().Be(small.Queries.Count);
        small.Queries.Count.Should().Be(9, "header + core + budget + prior + disallow + fee lines + roster + rates + previous lines");
        large.Queries.Select(Classify).Should().BeEquivalentTo(small.Queries.Select(Classify));
    }

    [Fact]
    public async Task AHistoryTooLargeToPage_IsReportedNotSummarisedFromAPartialRead()
    {
        var h = FullFixture();
        h.DisallowNeverEnds = true;

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.DisallowHistory.Code.Should().Be(InvoiceContextService.CodeDisallowTooLarge);
        h.Count(Kind.Disallow).Should().Be(InvoiceContextService.MaxPages);
        context.PriorInvoices.Status.Should().Be(ContextSectionStatus.Available);
    }

    [Fact]
    public async Task ARateCardReadTooLargeToPage_IsReported_NotSummarisedFromAPartialRead()
    {
        var h = FullFixture();
        h.Intercept = (kind, _, _) => kind == Kind.Rates
            ? Task.FromResult(new EntityCollection(h.RateRows) { MoreRecords = true, PagingCookie = "c" })
            : null;

        var context = (await h.Service().GetAsync(InvoiceId, Caller))!;

        context.RateHistory.Status.Should().Be(ContextSectionStatus.Unavailable);
        context.RateHistory.Code.Should().Be(InvoiceContextService.CodeRateTooLarge);
        context.DisallowHistory.Status.Should().Be(ContextSectionStatus.Available);
    }

    [Fact]
    public async Task SameData_GivesTheSameResponse()
    {
        var first = JsonSerializer.Serialize(await FullFixture().Service().GetAsync(InvoiceId, Caller));
        var second = JsonSerializer.Serialize(await FullFixture().Service().GetAsync(InvoiceId, Caller));

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
