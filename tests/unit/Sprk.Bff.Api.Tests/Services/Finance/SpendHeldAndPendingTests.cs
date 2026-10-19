using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 049, owner decisions D-102 (held invoices do not count toward spend, as amended by D-103) and D-104 (Pending
/// Spend: In Review invoices that are not held, kept apart from actual spend). Both spend-sum paths are exercised on in-memory rows:
/// (a) the rollup's invoice queries (<see cref="FinanceRollupService"/>, which writes the stored columns) and (b) the grouped
/// FetchXML the Signal evaluator reads through <see cref="FinanceBaseValues.ReadAsync"/> / <see cref="FinanceBaseValues.ReadPendingSpendAsync"/>.
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038): these decide what a Threshold Signal and the matter form say about a budget.</para>
/// <para><b>What the in-memory evaluator is.</b> A tiny interpreter of exactly the query shapes under test (one invoice entity, one
/// optional LEFT OUTER / inner link to the submissions, <c>eq</c> / <c>in</c> / <c>null</c> conditions, a grouped sum). It joins the
/// way SQL does, producing one row per matching linked row, so the double-count hazard of a plain join is real here and the
/// <see cref="ANaivePlainJoin_WouldCountAnInvoiceOncePerLinkedSubmission_ProvingTheEvaluatorCanSeeTheHazard"/> control proves it.
/// It cannot prove Dataverse accepts the FetchXML; that needs a live read, recorded in the task notes.</para>
/// </remarks>
public class SpendHeldAndPendingTests
{
    private static readonly Guid Matter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid OtherMatter = Guid.Parse("b68299c6-bafb-f011-8407-7c1e520aa4df");
    private static readonly Guid Project = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- the in-memory world ----

    private sealed class Store
    {
        public List<Entity> Invoices { get; } = new();
        public List<Entity> Submissions { get; } = new();

        public Entity Invoice(decimal amount, int statusCode, string parentEntity = "sprk_matter", Guid? parent = null)
        {
            var invoice = new Entity("sprk_invoice", Guid.NewGuid())
            {
                ["sprk_totalamount"] = new Money(amount),
                ["statuscode"] = new OptionSetValue(statusCode),
                [parentEntity] = new EntityReference(parentEntity, parent ?? Matter),
            };
            Invoices.Add(invoice);
            return invoice;
        }

        public Entity Submission(Entity invoice, InvoiceSubmissionStatus status, string? reason)
        {
            var id = Guid.NewGuid();
            var submission = new Entity("sprk_invoicesubmission", id)
            {
                ["sprk_invoicesubmissionid"] = id,   // Dataverse returns the key as an attribute; the null check reads it
                ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice.Id),
                ["sprk_processingstatus"] = new OptionSetValue((int)status),
            };
            if (reason is not null)
            {
                submission["sprk_outcomereasoncode"] = reason;
            }

            Submissions.Add(submission);
            return submission;
        }

        public Entity Blocked(Entity invoice) => Submission(invoice, InvoiceSubmissionStatus.Rejected, FinanceBaseValues.BlockedReasonCode);

        private sealed record Cond(string? Alias, string Attribute, string Operator, IReadOnlyList<string> Values);

        private sealed record Link(string Alias, string From, string To, bool Outer, IReadOnlyList<Cond> Conditions);

        private static string Text(object? value) => value switch
        {
            null => "",
            EntityReference r => r.Id.ToString("D"),
            OptionSetValue o => o.Value.ToString(CultureInfo.InvariantCulture),
            Money m => m.Value.ToString(CultureInfo.InvariantCulture),
            Guid g => g.ToString("D"),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()!,
        };

        private static bool Matches(Cond c, Entity row)
        {
            var value = row.Contains(c.Attribute) ? row[c.Attribute] : null;
            return c.Operator switch
            {
                "null" => value is null,
                "eq" => value is not null && Text(value) == c.Values.Single(),
                "in" => value is not null && c.Values.Contains(Text(value)),
                _ => throw new NotSupportedException("operator " + c.Operator),
            };
        }

        private IEnumerable<(Entity Invoice, Entity? Held)> Join(Link? link) =>
            Invoices.SelectMany<Entity, (Entity Invoice, Entity? Held)>(invoice =>
            {
                if (link is null)
                {
                    return new[] { (invoice, (Entity?)null) };
                }

                link.To.Should().Be("sprk_invoiceid");
                var matches = Submissions
                    .Where(s => Text(s.Contains(link.From) ? s[link.From] : null) == invoice.Id.ToString("D"))
                    .Where(s => link.Conditions.All(c => Matches(c, s)))
                    .Select(s => (invoice, (Entity?)s))
                    .ToList();
                return matches.Count > 0 ? matches : link.Outer ? new List<(Entity, Entity?)> { (invoice, null) } : new List<(Entity, Entity?)>();
            });

        private IEnumerable<(Entity Invoice, Entity? Held)> Filter(Link? link, IReadOnlyList<Cond> root) =>
            Join(link).Where(row => root.All(c =>
                c.Alias is null ? Matches(c, row.Invoice) : c.Alias == link?.Alias && (row.Held is null ? c.Operator == "null" : Matches(c, row.Held))));

        /// <summary>Runs a rollup <see cref="QueryExpression"/>; returns the invoice of every joined row (so a duplicate shows).</summary>
        public List<Entity> Run(QueryExpression query)
        {
            query.EntityName.Should().Be("sprk_invoice");
            query.Criteria.Filters.Should().BeEmpty("the evaluator handles one flat AND filter");
            query.Criteria.FilterOperator.Should().Be(LogicalOperator.And);
            Link? link = null;
            if (query.LinkEntities.Count > 0)
            {
                var l = query.LinkEntities.Single();
                l.LinkToEntityName.Should().Be("sprk_invoicesubmission");
                l.LinkCriteria.Filters.Should().BeEmpty();
                // QueryExpression: LinkFromAttributeName is on the root (invoice), LinkToAttributeName on the linked submission;
                // Link.From / Link.To follow the FetchXML reading (from = on the linked entity, to = on the root).
                link = new Link(l.EntityAlias, l.LinkToAttributeName, l.LinkFromAttributeName,
                    l.JoinOperator == JoinOperator.LeftOuter,
                    l.LinkCriteria.Conditions.Select(c => new Cond(null, c.AttributeName, Op(c.Operator), c.Values.Select(Text).ToList())).ToList());
            }

            var root = query.Criteria.Conditions
                .Select(c => new Cond(string.IsNullOrEmpty(c.EntityName) ? null : c.EntityName, c.AttributeName, Op(c.Operator), c.Values.Select(Text).ToList()))
                .ToList();
            return Filter(link, root).Select(r => r.Invoice).ToList();
        }

        /// <summary>The rollup's sum over a query: what <c>FinanceBaseValues.SumMoney</c> makes of the returned rows.</summary>
        public decimal Sum(QueryExpression query) => FinanceBaseValues.SumMoney(Run(query), "sprk_totalamount");

        private static string Op(ConditionOperator op) => op switch
        {
            ConditionOperator.Equal => "eq",
            ConditionOperator.In => "in",
            ConditionOperator.Null => "null",
            _ => throw new NotSupportedException("operator " + op),
        };

        /// <summary>Runs a grouped aggregate FetchXML: the sum of <c>sprk_totalamount</c> per parent, in the aliased shape Dataverse returns.</summary>
        public EntityCollection RunFetch(string fetchXml)
        {
            var fetch = XElement.Parse(fetchXml);
            fetch.Attribute("aggregate")!.Value.Should().Be("true");
            var entity = fetch.Element("entity")!;
            entity.Attribute("name")!.Value.Should().Be("sprk_invoice");

            Link? link = null;
            if (entity.Element("link-entity") is { } le)
            {
                link = new Link((string)le.Attribute("alias")!, (string)le.Attribute("from")!, (string)le.Attribute("to")!,
                    (string)le.Attribute("link-type")! == "outer", le.Element("filter")!.Elements("condition").Select(Parse).ToList());
            }

            var root = entity.Element("filter")!.Elements("condition").Select(Parse).ToList();
            var groupBy = entity.Elements("attribute").Single(a => a.Attribute("groupby") is not null).Attribute("name")!.Value;
            var result = new EntityCollection();
            foreach (var group in Filter(link, root).GroupBy(r => Text(r.Invoice.Contains(groupBy) ? r.Invoice[groupBy] : null)))
            {
                var sample = group.First().Invoice;
                result.Entities.Add(new Entity("sprk_invoice")
                {
                    ["parent"] = new AliasedValue("sprk_invoice", groupBy, sample.Contains(groupBy) ? sample[groupBy] : null),
                    ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", new Money(group.Sum(r => ((Money)r.Invoice["sprk_totalamount"]).Value))),
                });
            }

            return result;
        }

        private static Cond Parse(XElement c) => new(
            (string?)c.Attribute("entityname"), (string)c.Attribute("attribute")!, (string)c.Attribute("operator")!,
            c.Attribute("value") is { } v ? new[] { v.Value } : c.Elements("value").Select(x => x.Value).ToList());

        /// <summary>An <see cref="IGenericEntityService"/> whose FetchXML reads run against this store.</summary>
        public IGenericEntityService AsService()
        {
            var service = new Mock<IGenericEntityService>();
            service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .Returns((FetchExpression fetch, CancellationToken _) => Task.FromResult(RunFetch(fetch.Query)));
            return service.Object;
        }
    }

    private static async Task<decimal> LiveCountedSpend(Store store, Guid parent = default) =>
        (await FinanceBaseValues.ReadAsync(store.AsService(), "sprk_matter", new[] { parent == default ? Matter : parent },
            needSpend: true, needBudget: false, CancellationToken.None))[parent == default ? Matter : parent].Spend;

    private static async Task<decimal> LivePendingSpend(Store store, string parentEntity = "sprk_matter", Guid parent = default) =>
        (await FinanceBaseValues.ReadPendingSpendAsync(store.AsService(), parentEntity, new[] { parent == default ? Matter : parent },
            CancellationToken.None))[parent == default ? Matter : parent];

    private static decimal RollupCountedSpend(Store store, string lookup = "sprk_matter", Guid parent = default) =>
        store.Sum(FinanceRollupService.BuildInvoiceQuery(parent == default ? Matter : parent, lookup));

    private static decimal RollupPendingSpend(Store store, string lookup = "sprk_matter", Guid parent = default) =>
        store.Sum(FinanceRollupService.BuildPendingInvoiceQuery(parent == default ? Matter : parent, lookup));

    // ---- D-102 (as amended by D-103): a held invoice is not counted spend, on BOTH sum paths ----

    [Fact(DisplayName = "D-102 must-fire: a held In Review invoice is excluded from the rollup's counted spend (stored sprk_totalspendtodate)")]
    public void D102_AHeldInReviewInvoice_IsExcludedFromTheRollupCountedSpend()
    {
        var store = new Store();
        store.Blocked(store.Invoice(100m, InvoiceStatusCodes.InReview));

        RollupCountedSpend(store).Should().Be(0m);
        store.Run(FinanceRollupService.BuildInvoiceQuery(Matter, "sprk_matter")).Should().BeEmpty();
    }

    [Fact(DisplayName = "D-102 must-fire: a held In Review invoice is excluded from the live Threshold input (FinanceBaseValues.ReadAsync)")]
    public async Task D102_AHeldInReviewInvoice_IsExcludedFromTheLiveCountedSpend()
    {
        var store = new Store();
        store.Blocked(store.Invoice(100m, InvoiceStatusCodes.InReview));

        (await LiveCountedSpend(store)).Should().Be(0m);
    }

    [Fact(DisplayName = "D-102 must-not-fire: a released invoice that reaches Approved is counted, on both paths")]
    public async Task D102_AReleasedInvoiceThatReachesApproved_IsCounted_OnBothPaths()
    {
        var store = new Store();
        var invoice = store.Invoice(100m, InvoiceStatusCodes.InReview);
        var submission = store.Blocked(invoice);
        RollupCountedSpend(store).Should().Be(0m, "held: In Review, blocked submission");

        // Override (105) releases it: the submission becomes Accepted; the lifecycle service then approves the invoice.
        submission["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);
        invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);

        RollupCountedSpend(store).Should().Be(100m);
        (await LiveCountedSpend(store)).Should().Be(100m);
        (await LivePendingSpend(store)).Should().Be(0m, "an Approved invoice is actual spend, not pending");
    }

    [Fact(DisplayName = "D-102: counted spend carries NO held predicate (D-103 makes it automatic); adding In Review back would admit the held row and fail the must-fire tests")]
    public void D102_CountedSpendQueries_HaveNoHeldPredicate_AndAdmitNoInReview()
    {
        FinanceRollupService.BuildInvoiceQuery(Matter, "sprk_matter").LinkEntities.Should().BeEmpty();
        XElement.Parse(FinanceBaseValues.BuildGroupedSumFetchXml("sprk_matter", forInvoices: true, new[] { Matter }))
            .Descendants("link-entity").Should().BeEmpty();
        FinanceBaseValues.CountedInvoiceStatuses.Should().NotContain(InvoiceStatusCodes.InReview);
    }

    // ---- D-104: Pending Spend = In Review and NOT held, on BOTH paths ----

    [Fact(DisplayName = "D-104 must-fire: a held In Review invoice is not in pending spend (rollup and live)")]
    public async Task D104_AHeldInReviewInvoice_IsNotInPendingSpend_OnBothPaths()
    {
        var store = new Store();
        store.Blocked(store.Invoice(100m, InvoiceStatusCodes.InReview));

        RollupPendingSpend(store).Should().Be(0m);
        (await LivePendingSpend(store)).Should().Be(0m);
    }

    [Fact(DisplayName = "D-104 must-fire: an Approved invoice (and every other non-In-Review status) is not in pending spend (rollup and live)")]
    public async Task D104_AnApprovedInvoice_IsNotInPendingSpend_OnBothPaths()
    {
        var store = new Store();
        foreach (var status in new[]
        {
            InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid, InvoiceStatusCodes.Rejected,
            InvoiceStatusCodes.ReturnedToFirm, InvoiceStatusCodes.Cancelled,
        })
        {
            store.Invoice(1000m, status);
        }

        RollupPendingSpend(store).Should().Be(0m);
        (await LivePendingSpend(store)).Should().Be(0m);
    }

    [Fact(DisplayName = "D-104 must-not-fire: an unheld In Review invoice is counted in pending spend (rollup and live), and not in actual spend")]
    public async Task D104_AnUnheldInReviewInvoice_IsCountedAsPending_OnBothPaths()
    {
        var store = new Store();
        store.Invoice(50m, InvoiceStatusCodes.InReview);

        RollupPendingSpend(store).Should().Be(50m);
        (await LivePendingSpend(store)).Should().Be(50m);
        RollupCountedSpend(store).Should().Be(0m, "pending is kept separate from actual spend");
        (await LiveCountedSpend(store)).Should().Be(0m);
    }

    [Fact(DisplayName = "D-104 must-not-fire: a submission that is Rejected for another reason, or Accepted/Failed, does not hold its In Review invoice")]
    public async Task D104_OnlyABlockedSubmission_HoldsAnInvoice()
    {
        var store = new Store();
        store.Submission(store.Invoice(1m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Rejected, "invoice.read.unreadable");
        store.Submission(store.Invoice(2m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Rejected, null);
        store.Submission(store.Invoice(4m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Accepted, FinanceBaseValues.BlockedReasonCode);
        store.Submission(store.Invoice(8m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Processing, FinanceBaseValues.BlockedReasonCode);

        RollupPendingSpend(store).Should().Be(15m);
        (await LivePendingSpend(store)).Should().Be(15m);
    }

    [Fact(DisplayName = "D-104 double-count: an invoice with several linked submissions is counted exactly once when none blocks, and excluded entirely when one blocks")]
    public async Task D104_AnInvoiceWithSeveralSubmissions_IsNeverCountedTwice_NorHalfCounted()
    {
        var store = new Store();
        var twoUnblocked = store.Invoice(7m, InvoiceStatusCodes.InReview);
        store.Submission(twoUnblocked, InvoiceSubmissionStatus.Accepted, null);
        store.Submission(twoUnblocked, InvoiceSubmissionStatus.Failed, "ebilling.processing.failed");
        var oneBlockedOneNot = store.Invoice(11m, InvoiceStatusCodes.InReview);
        store.Submission(oneBlockedOneNot, InvoiceSubmissionStatus.Accepted, null);
        store.Blocked(oneBlockedOneNot);
        var twoBlocked = store.Invoice(13m, InvoiceStatusCodes.InReview);
        store.Blocked(twoBlocked);
        store.Blocked(twoBlocked);

        RollupPendingSpend(store).Should().Be(7m, "7 once (not 14); 11 and 13 are held");
        (await LivePendingSpend(store)).Should().Be(7m);
        store.Run(FinanceRollupService.BuildPendingInvoiceQuery(Matter, "sprk_matter")).Should().ContainSingle();
    }

    [Fact(DisplayName = "D-104 control: a naive plain join WOULD count an invoice once per linked submission (the evaluator can see the hazard)")]
    public void ANaivePlainJoin_WouldCountAnInvoiceOncePerLinkedSubmission_ProvingTheEvaluatorCanSeeTheHazard()
    {
        var store = new Store();
        var invoice = store.Invoice(7m, InvoiceStatusCodes.InReview);
        store.Submission(invoice, InvoiceSubmissionStatus.Accepted, null);
        store.Submission(invoice, InvoiceSubmissionStatus.Failed, null);

        var naive = FinanceRollupService.BuildPendingInvoiceQuery(Matter, "sprk_matter");
        naive.LinkEntities.Single().LinkCriteria.Conditions.Clear();   // every submission matches the link
        naive.Criteria.Conditions.Remove(naive.Criteria.Conditions.Single(c => c.EntityName == "held"));   // and no null check

        store.Sum(naive).Should().Be(14m);
    }

    [Fact(DisplayName = "D-104: pending spend is grouped per parent; a parent with none is 0; matter and project stay apart")]
    public async Task D104_PendingSpend_IsPerParent()
    {
        var store = new Store();
        store.Invoice(5m, InvoiceStatusCodes.InReview);
        store.Invoice(6m, InvoiceStatusCodes.InReview, "sprk_project", Project);
        store.Invoice(9m, InvoiceStatusCodes.InReview, "sprk_matter", OtherMatter);

        var matters = await FinanceBaseValues.ReadPendingSpendAsync(
            store.AsService(), "sprk_matter", new[] { Matter, OtherMatter, Guid.NewGuid() }, CancellationToken.None);
        matters.Values.OrderBy(v => v).Should().Equal(0m, 5m, 9m);
        matters[Matter].Should().Be(5m);
        matters[OtherMatter].Should().Be(9m);

        (await LivePendingSpend(store, "sprk_project", Project)).Should().Be(6m);
        RollupPendingSpend(store, "sprk_project", Project).Should().Be(6m);
        RollupPendingSpend(store, "sprk_matter", OtherMatter).Should().Be(9m);
    }

    [Fact(DisplayName = "D-104: the rollup and the live path agree on the same rows (mixed world)")]
    public async Task D104_BothPaths_AgreeOnAMixedWorld()
    {
        var store = new Store();
        store.Blocked(store.Invoice(100m, InvoiceStatusCodes.InReview));
        store.Invoice(50m, InvoiceStatusCodes.InReview);
        store.Invoice(200m, InvoiceStatusCodes.Approved);
        var released = store.Invoice(30m, InvoiceStatusCodes.Approved);
        store.Submission(released, InvoiceSubmissionStatus.Accepted, FinanceBaseValues.BlockedReasonCode);
        store.Invoice(9000m, InvoiceStatusCodes.Rejected);

        RollupPendingSpend(store).Should().Be(50m);
        (await LivePendingSpend(store)).Should().Be(50m);
        RollupCountedSpend(store).Should().Be(230m);
        (await LiveCountedSpend(store)).Should().Be(230m);
    }

    // ---- the shared definition and the query shapes ----

    [Fact(DisplayName = "D-104: the pending set is In Review only, apart from the counted set, and the held predicate is one definition both paths use")]
    public void D104_PendingStatuses_AndHeldPredicate_AreDefinedOnceInFinanceBaseValues()
    {
        FinanceBaseValues.PendingInvoiceStatuses.Should().Equal(InvoiceStatusCodes.InReview);
        FinanceBaseValues.PendingInvoiceStatuses.Intersect(FinanceBaseValues.CountedInvoiceStatuses).Should().BeEmpty();
        FinanceBaseValues.PendingSpendColumn.Should().Be("sprk_pendingspend");
        FinanceBaseValues.HeldSubmissionConditions.Should().Equal(
            ("sprk_processingstatus", (object)100000003), ("sprk_outcomereasoncode", "ebilling.submission.blocked"));

        var held = FinanceBaseValues.HeldSubmissionConditions.Select(c => (c.Column, Convert.ToString(c.Value, CultureInfo.InvariantCulture)!)).ToList();

        var rollup = FinanceRollupService.BuildPendingInvoiceQuery(Matter, "sprk_matter");
        var link = rollup.LinkEntities.Single();
        link.LinkToEntityName.Should().Be("sprk_invoicesubmission");
        link.JoinOperator.Should().Be(JoinOperator.LeftOuter);
        link.EntityAlias.Should().Be("held");
        link.LinkCriteria.Conditions.Select(c => (c.AttributeName, Convert.ToString(c.Values.Single(), CultureInfo.InvariantCulture)!)).Should().Equal(held);
        rollup.Criteria.Conditions.Should().Contain(c => c.EntityName == "held" && c.AttributeName == "sprk_invoicesubmissionid" && c.Operator == ConditionOperator.Null);
        rollup.Criteria.Conditions.Single(c => c.AttributeName == "statuscode").Values.Cast<int>().Should().Equal(InvoiceStatusCodes.InReview);

        var fetch = XElement.Parse(FinanceBaseValues.BuildGroupedPendingSumFetchXml("sprk_matter", new[] { Matter }));
        var le = fetch.Descendants("link-entity").Single();
        ((string)le.Attribute("name")!, (string)le.Attribute("from")!, (string)le.Attribute("to")!, (string)le.Attribute("link-type")!, (string)le.Attribute("alias")!)
            .Should().Be(("sprk_invoicesubmission", "sprk_invoice", "sprk_invoiceid", "outer", "held"));
        le.Element("filter")!.Elements("condition").Select(c => ((string)c.Attribute("attribute")!, (string)c.Attribute("value")!)).Should().Equal(held,
            "the blocking conditions are INSIDE the link (a where-clause filter would turn the outer join inner)");
        fetch.Element("entity")!.Element("filter")!.Elements("condition").Should().Contain(c =>
            (string?)c.Attribute("entityname") == "held" && (string)c.Attribute("attribute")! == "sprk_invoicesubmissionid" && (string)c.Attribute("operator")! == "null");
        fetch.Descendants("condition").Single(c => (string)c.Attribute("attribute")! == "statuscode").Elements("value").Select(v => int.Parse(v.Value))
            .Should().Equal(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "D-104: the grouped pending query keeps the parent lookup, grouping and aggregate of the counted query")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void D104_TheGroupedPendingQuery_IsTheCountedShape_WithPendingStatusesAndTheHeldExclusion(string parent)
    {
        var fetch = XElement.Parse(FinanceBaseValues.BuildGroupedPendingSumFetchXml(parent, new[] { Matter }));
        fetch.Attribute("aggregate")!.Value.Should().Be("true");
        fetch.Element("entity")!.Attribute("name")!.Value.Should().Be("sprk_invoice");
        fetch.Element("entity")!.Elements("attribute").Select(a => (a.Attribute("name")!.Value, a.Attribute("aggregate")?.Value, a.Attribute("groupby")?.Value))
            .Should().BeEquivalentTo(new (string, string?, string?)[] { ("sprk_totalamount", "sum", null), (parent, null, "true") });
        FinanceRollupService.BuildPendingInvoiceQuery(Matter, parent).Criteria.Conditions.Single(c => c.AttributeName == parent)
            .Operator.Should().Be(ConditionOperator.Equal);
    }

    // ---- fail closed (the reader contract) ----

    [Fact(DisplayName = "D-104 fail-closed: an unreadable shape in the pending result throws, never reads as zero")]
    public async Task D104_AnUnreadablePendingShape_Throws_NotZero()
    {
        var bad = new EntityCollection();
        bad.Entities.Add(new Entity("sprk_invoice")
        {
            ["parent"] = new AliasedValue("sprk_invoice", "sprk_matter", "not-a-guid"),
            ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", new Money(5m)),
        });
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(bad);

        await FluentActions.Invoking(() => FinanceBaseValues.ReadPendingSpendAsync(service.Object, "sprk_matter", new[] { Matter }, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*parent*unexpected type*String*");
    }

    [Fact(DisplayName = "D-104 fail-closed: a Dataverse failure on the pending read propagates")]
    public async Task D104_ADataverseFailure_Propagates()
    {
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse"));

        await FluentActions.Invoking(() => FinanceBaseValues.ReadPendingSpendAsync(service.Object, "sprk_project", new[] { Project }, CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();
    }

    [Fact(DisplayName = "D-104: a parent entity that is neither matter nor project is refused")]
    public async Task D104_AnUnknownParentEntity_IsRefused()
    {
        await FluentActions.Invoking(() => FinanceBaseValues.ReadPendingSpendAsync(new Store().AsService(), "sprk_event", new[] { Matter }, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
