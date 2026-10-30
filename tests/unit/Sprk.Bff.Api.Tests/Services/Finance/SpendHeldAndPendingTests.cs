using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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

        /// <summary><paramref name="amount"/> is the billed total; <paramref name="approved"/> is the approved total (D-130), absent when null.</summary>
        public Entity Invoice(decimal amount, int statusCode, string parentEntity = "sprk_matter", Guid? parent = null, decimal? approved = null)
        {
            var invoice = new Entity("sprk_invoice", Guid.NewGuid())
            {
                ["sprk_totalamount"] = new Money(amount),
                ["statuscode"] = new OptionSetValue(statusCode),
                [parentEntity] = new EntityReference(parentEntity, parent ?? Matter),
            };
            if (approved is { } approvedTotal)
            {
                invoice["sprk_approvedtotal"] = new Money(approvedTotal);
            }

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
                "not-null" => value is not null,
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
            // Dataverse returns only the selected columns: a column the query forgets to select reads as empty. Project the same way, so
            // a rollup that left sprk_approvedtotal out of its ColumnSet would silently fall back to the billed total HERE too.
            return Filter(link, root).Select(r => Project(r.Invoice, query.ColumnSet)).ToList();
        }

        private static Entity Project(Entity invoice, ColumnSet columns)
        {
            if (columns.AllColumns)
            {
                return invoice;
            }

            var projected = new Entity(invoice.LogicalName, invoice.Id);
            foreach (var column in columns.Columns.Where(invoice.Contains))
            {
                projected[column] = invoice[column];
            }

            return projected;
        }

        /// <summary>The rollup's sum over a query: what <c>FinanceBaseValues.SumSpend</c> (the D-130 amount rule) makes of the returned rows.</summary>
        public decimal Sum(QueryExpression query) => FinanceBaseValues.SumSpend(Run(query));

        private static string Op(ConditionOperator op) => op switch
        {
            ConditionOperator.Equal => "eq",
            ConditionOperator.In => "in",
            ConditionOperator.Null => "null",
            _ => throw new NotSupportedException("operator " + op),
        };

        /// <summary>Runs a grouped aggregate FetchXML: the sum of the aggregated column per parent (a SQL sum: nulls are skipped, a group
        /// of only nulls sums to null), in the aliased shape Dataverse returns.</summary>
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
            var summed = entity.Elements("attribute").Single(a => (string?)a.Attribute("aggregate") == "sum").Attribute("name")!.Value;
            var result = new EntityCollection();
            foreach (var group in Filter(link, root).GroupBy(r => Text(r.Invoice.Contains(groupBy) ? r.Invoice[groupBy] : null)))
            {
                var sample = group.First().Invoice;
                result.Entities.Add(new Entity("sprk_invoice")
                {
                    ["parent"] = new AliasedValue("sprk_invoice", groupBy, sample.Contains(groupBy) ? sample[groupBy] : null),
                    ["total"] = new AliasedValue("sprk_invoice", summed, SqlSum(group.Select(r => r.Invoice.GetAttributeValue<Money>(summed)))),
                });
            }

            return result;
        }

        private static Money? SqlSum(IEnumerable<Money?> values)
        {
            var present = values.Where(v => v is not null).ToList();
            return present.Count == 0 ? null : new Money(present.Sum(v => v!.Value));
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
        foreach (var part in FinanceBaseValues.SpendAmountPartsInReadOrder)
        {
            XElement.Parse(FinanceBaseValues.BuildGroupedSpendFetchXml("sprk_matter", part, new[] { Matter }))
                .Descendants("link-entity").Should().BeEmpty();
        }

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

    [Fact(DisplayName = "D-104 must-not-fire: a submission with no blocked code (or another code), or an Accepted/Failed/Received one, does not hold its In Review invoice")]
    public async Task D104_OnlyABlockedSubmission_HoldsAnInvoice()
    {
        var store = new Store();
        store.Submission(store.Invoice(1m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Rejected, "invoice.read.unreadable");
        store.Submission(store.Invoice(2m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Rejected, null);
        store.Submission(store.Invoice(4m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Accepted, FinanceBaseValues.BlockedReasonCode);
        store.Submission(store.Invoice(8m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Processing, null);
        store.Submission(store.Invoice(16m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Processing, "invoice.read.unreadable");
        store.Submission(store.Invoice(32m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Failed, FinanceBaseValues.BlockedReasonCode);
        store.Submission(store.Invoice(64m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Received, FinanceBaseValues.BlockedReasonCode);

        RollupPendingSpend(store).Should().Be(127m);
        (await LivePendingSpend(store)).Should().Be(127m);
    }

    // D-130(a): the persist transaction writes the blocked code and sets the invoice In Review, but the submission stays Processing
    // until the after-steps' final write (permanently so when that write is poisoned). The invoice is held for that whole time.
    [Fact(DisplayName = "D-130(a) must-fire: a Processing submission carrying the blocked code holds its In Review invoice (rollup and live); Rejected still does")]
    public async Task D130a_AProcessingSubmissionWithTheBlockedCode_HoldsItsInvoice_OnBothPaths()
    {
        var store = new Store();
        store.Submission(store.Invoice(100m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Processing, FinanceBaseValues.BlockedReasonCode);
        store.Submission(store.Invoice(200m, InvoiceStatusCodes.InReview), InvoiceSubmissionStatus.Rejected, FinanceBaseValues.BlockedReasonCode);
        store.Invoice(50m, InvoiceStatusCodes.InReview);

        RollupPendingSpend(store).Should().Be(50m);
        (await LivePendingSpend(store)).Should().Be(50m);
    }

    [Fact(DisplayName = "D-130(a): a blocked Processing submission releases on Override (Accepted) - the invoice then leaves the held set on both paths")]
    public async Task D130a_AfterOverride_TheProcessingBlockedInvoice_IsNoLongerHeld()
    {
        var store = new Store();
        var invoice = store.Invoice(100m, InvoiceStatusCodes.InReview);
        var submission = store.Submission(invoice, InvoiceSubmissionStatus.Processing, FinanceBaseValues.BlockedReasonCode);
        RollupPendingSpend(store).Should().Be(0m);
        (await LivePendingSpend(store)).Should().Be(0m);

        submission["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);

        RollupPendingSpend(store).Should().Be(100m, "Override moved the submission to Accepted: not held, still In Review, so pending");
        (await LivePendingSpend(store)).Should().Be(100m);
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
        var held = FinanceBaseValues.HeldSubmissionConditions
            .Select(c => (c.Column, string.Join(",", c.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)!)))).ToList();
        held.Should().Equal(
            ("sprk_processingstatus", "100000001,100000003"), ("sprk_outcomereasoncode", "ebilling.submission.blocked"));

        var rollup = FinanceRollupService.BuildPendingInvoiceQuery(Matter, "sprk_matter");
        var link = rollup.LinkEntities.Single();
        link.LinkToEntityName.Should().Be("sprk_invoicesubmission");
        link.JoinOperator.Should().Be(JoinOperator.LeftOuter);
        link.EntityAlias.Should().Be("held");
        link.LinkCriteria.Conditions.Should().OnlyContain(c => c.Operator == ConditionOperator.In);
        link.LinkCriteria.Conditions.Select(c => (c.AttributeName, string.Join(",", c.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)!)))).Should().Equal(held);
        rollup.Criteria.Conditions.Should().Contain(c => c.EntityName == "held" && c.AttributeName == "sprk_invoicesubmissionid" && c.Operator == ConditionOperator.Null);
        rollup.Criteria.Conditions.Single(c => c.AttributeName == "statuscode").Values.Cast<int>().Should().Equal(InvoiceStatusCodes.InReview);

        foreach (var part in FinanceBaseValues.SpendAmountPartsInReadOrder)
        {
            AssertPendingFetchShape(XElement.Parse(FinanceBaseValues.BuildGroupedPendingSpendFetchXml("sprk_matter", part, new[] { Matter })), held);
        }
    }

    private static void AssertPendingFetchShape(XElement fetch, IReadOnlyList<(string Column, string)> held)
    {
        var le = fetch.Descendants("link-entity").Single();
        ((string)le.Attribute("name")!, (string)le.Attribute("from")!, (string)le.Attribute("to")!, (string)le.Attribute("link-type")!, (string)le.Attribute("alias")!)
            .Should().Be(("sprk_invoicesubmission", "sprk_invoice", "sprk_invoiceid", "outer", "held"));
        le.Element("filter")!.Elements("condition").Should().OnlyContain(c => (string)c.Attribute("operator")! == "in");
        le.Element("filter")!.Elements("condition").Select(c => ((string)c.Attribute("attribute")!, string.Join(",", c.Elements("value").Select(v => v.Value)))).Should().Equal(held,
            "the blocking conditions are INSIDE the link (a where-clause filter would turn the outer join inner)");
        fetch.Element("entity")!.Element("filter")!.Elements("condition").Should().Contain(c =>
            (string?)c.Attribute("entityname") == "held" && (string)c.Attribute("attribute")! == "sprk_invoicesubmissionid" && (string)c.Attribute("operator")! == "null");
        fetch.Descendants("condition").Single(c => (string)c.Attribute("attribute")! == "statuscode").Elements("value").Select(v => int.Parse(v.Value))
            .Should().Equal(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "D-104: the grouped pending query keeps the parent lookup, grouping and aggregate of the counted query")]
    [InlineData("sprk_matter", FinanceBaseValues.SpendAmountPart.ApprovedTotal, "sprk_approvedtotal")]
    [InlineData("sprk_matter", FinanceBaseValues.SpendAmountPart.BilledWhereNoApprovedTotal, "sprk_totalamount")]
    [InlineData("sprk_project", FinanceBaseValues.SpendAmountPart.ApprovedTotal, "sprk_approvedtotal")]
    [InlineData("sprk_project", FinanceBaseValues.SpendAmountPart.BilledWhereNoApprovedTotal, "sprk_totalamount")]
    public void D104_TheGroupedPendingQuery_IsTheCountedShape_WithPendingStatusesAndTheHeldExclusion(
        string parent, FinanceBaseValues.SpendAmountPart part, string summed)
    {
        var fetch = XElement.Parse(FinanceBaseValues.BuildGroupedPendingSpendFetchXml(parent, part, new[] { Matter }));
        fetch.Attribute("aggregate")!.Value.Should().Be("true");
        fetch.Element("entity")!.Attribute("name")!.Value.Should().Be("sprk_invoice");
        fetch.Element("entity")!.Elements("attribute").Select(a => (a.Attribute("name")!.Value, a.Attribute("aggregate")?.Value, a.Attribute("groupby")?.Value))
            .Should().BeEquivalentTo(new (string, string?, string?)[] { (summed, "sum", null), (parent, null, "true") });
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

    // ---- D-130 (2): spend sums the APPROVED total, falling back to the billed total where it is empty, on BOTH paths ----
    // Each test asserts the rollup path (FinanceRollupService's query + FinanceBaseValues.SumSpend) and the live path
    // (FinanceBaseValues.ReadAsync / ReadPendingSpendAsync through the grouped FetchXML) on the same rows.

    private static async Task BothPathsCountedSpendShouldBe(Store store, decimal expected, string because = "")
    {
        RollupCountedSpend(store).Should().Be(expected, "rollup path " + because);
        (await LiveCountedSpend(store)).Should().Be(expected, "live path " + because);
    }

    private static async Task BothPathsPendingSpendShouldBe(Store store, decimal expected, string because = "")
    {
        RollupPendingSpend(store).Should().Be(expected, "rollup path " + because);
        (await LivePendingSpend(store)).Should().Be(expected, "live path " + because);
    }

    [Fact(DisplayName = "D-130 must-fire: an Approved invoice with a disallowed line counts its APPROVED total, not the billed total (both paths)")]
    public async Task D130_AnApprovedInvoiceWithADisallowedLine_CountsItsApprovedTotal()
    {
        var store = new Store();
        store.Invoice(1000m, InvoiceStatusCodes.Approved, approved: 800m);   // a 200 charge was disallowed

        await BothPathsCountedSpendShouldBe(store, 800m, "(billed 1000 would be the pre-D-130 answer)");
    }

    [Fact(DisplayName = "D-130 must-fire: an invoice with NO approved total counts its billed total (both paths)")]
    public async Task D130_AnInvoiceWithNoApprovedTotal_CountsItsBilledTotal()
    {
        var store = new Store();
        store.Invoice(1000m, InvoiceStatusCodes.Approved);

        await BothPathsCountedSpendShouldBe(store, 1000m);
    }

    [Fact(DisplayName = "D-130: an approved total of ZERO is a value, not an empty one: it counts 0 and does not fall back to the billed total (both paths)")]
    public async Task D130_AnApprovedTotalOfZero_CountsZero_NotTheBilledTotal()
    {
        var store = new Store();
        store.Invoice(500m, InvoiceStatusCodes.Approved, approved: 0m);   // every charge disallowed

        await BothPathsCountedSpendShouldBe(store, 0m);
    }

    [Fact(DisplayName = "D-130: mixed invoices sum correctly - approved where there is one, billed where there is not, other statuses and parents left out (both paths)")]
    public async Task D130_MixedInvoices_SumApprovedWhereSet_BilledWhereNot()
    {
        var store = new Store();
        store.Invoice(100m, InvoiceStatusCodes.Approved, approved: 80m);       //  80
        store.Invoice(50m, InvoiceStatusCodes.SentToAp);                       //  50 (no approved total)
        store.Invoice(300m, InvoiceStatusCodes.Paid, approved: 300m);          // 300 (nothing disallowed)
        store.Invoice(40m, InvoiceStatusCodes.Approved, approved: 0m);         //   0
        store.Invoice(999m, InvoiceStatusCodes.InReview, approved: 900m);      // pending, not counted
        store.Invoice(999m, InvoiceStatusCodes.Rejected, approved: 900m);      // not counted
        store.Invoice(999m, InvoiceStatusCodes.Approved, "sprk_matter", OtherMatter, approved: 900m);   // another matter
        store.Invoice(999m, InvoiceStatusCodes.Approved, "sprk_project", Project, approved: 900m);      // a project, not this matter

        await BothPathsCountedSpendShouldBe(store, 430m);
    }

    [Fact(DisplayName = "D-130 + D-127: an approved credit memo (negative approved total) LOWERS counted spend, with or without an approved total (both paths)")]
    public async Task D130_ACreditMemo_LowersSpend()
    {
        var store = new Store();
        store.Invoice(1000m, InvoiceStatusCodes.Approved);
        await BothPathsCountedSpendShouldBe(store, 1000m);

        store.Invoice(-250m, InvoiceStatusCodes.Approved, approved: -250m);    // credit memo with an approved total
        await BothPathsCountedSpendShouldBe(store, 750m, "(credit memo with an approved total)");

        store.Invoice(-100m, InvoiceStatusCodes.Paid);                         // credit memo with no approved total: billed, signed
        await BothPathsCountedSpendShouldBe(store, 650m, "(credit memo falling back to its billed total)");

        store.Invoice(-60m, InvoiceStatusCodes.Approved, approved: -40m);      // reviewer reduced a credit line (D-129): payable credit is smaller
        await BothPathsCountedSpendShouldBe(store, 610m, "(the approved credit, not the billed credit)");
    }

    [Fact(DisplayName = "D-130: the two paths agree on a seeded random world of invoices, with and without approved totals and credits")]
    public async Task D130_BothPaths_AgreeOnARandomWorld()
    {
        var random = new Random(130);
        var statuses = new[]
        {
            InvoiceStatusCodes.InReview, InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid,
            InvoiceStatusCodes.Rejected, InvoiceStatusCodes.ReturnedToFirm, InvoiceStatusCodes.Cancelled,
        };
        var store = new Store();
        decimal expectedCounted = 0m;
        decimal expectedPending = 0m;
        for (var i = 0; i < 200; i++)
        {
            var status = statuses[random.Next(statuses.Length)];
            var billed = random.Next(-300, 1000);
            decimal? approved = random.Next(3) == 0 ? null : billed - random.Next(0, 4) * 25;
            var invoice = store.Invoice(billed, status, approved: approved);
            var amount = approved ?? billed;
            if (FinanceBaseValues.CountedInvoiceStatuses.Contains(status))
            {
                expectedCounted += amount;
            }
            else if (status == InvoiceStatusCodes.InReview)
            {
                if (random.Next(4) == 0)
                {
                    store.Blocked(invoice);   // held: left out of pending
                }
                else
                {
                    if (random.Next(2) == 0) store.Submission(invoice, InvoiceSubmissionStatus.Accepted, null);   // several links: still once
                    expectedPending += amount;
                }
            }
        }

        await BothPathsCountedSpendShouldBe(store, expectedCounted);
        await BothPathsPendingSpendShouldBe(store, expectedPending);
    }

    [Fact(DisplayName = "D-130 pending (D-104): Pending Spend uses the same amount rule - approved total, else billed, credit memos signed - and still excludes held (both paths)")]
    public async Task D130_PendingSpend_UsesTheSameAmountRule()
    {
        var store = new Store();
        store.Invoice(100m, InvoiceStatusCodes.InReview, approved: 90m);      //  90
        store.Invoice(50m, InvoiceStatusCodes.InReview);                      //  50
        store.Invoice(70m, InvoiceStatusCodes.InReview, approved: 0m);        //   0
        store.Invoice(-30m, InvoiceStatusCodes.InReview, approved: -30m);     // -30 (credit memo)
        store.Blocked(store.Invoice(500m, InvoiceStatusCodes.InReview, approved: 400m));   // held: left out whatever its amount
        var resubmitted = store.Invoice(20m, InvoiceStatusCodes.InReview, approved: 15m);
        store.Submission(resubmitted, InvoiceSubmissionStatus.Accepted, null);
        store.Submission(resubmitted, InvoiceSubmissionStatus.Failed, "ebilling.processing.failed");   // two links: counted once, as 15
        store.Invoice(900m, InvoiceStatusCodes.Approved, approved: 800m);     // actual spend, not pending

        await BothPathsPendingSpendShouldBe(store, 125m);
        await BothPathsCountedSpendShouldBe(store, 800m);
    }

    [Fact(DisplayName = "D-130 D-102 must-fire stays: a held invoice with an approved total is still left out of counted spend (both paths)")]
    public async Task D130_AHeldInvoice_WithAnApprovedTotal_IsStillNotCountedSpend()
    {
        var store = new Store();
        store.Blocked(store.Invoice(100m, InvoiceStatusCodes.InReview, approved: 100m));

        await BothPathsCountedSpendShouldBe(store, 0m);
    }

    [Fact(DisplayName = "D-130: the rollup's invoice queries select BOTH amount columns (a missing column would read as empty and silently fall back to billed)")]
    public void D130_TheRollupQueries_SelectBothAmountColumns()
    {
        foreach (var query in new[]
        {
            FinanceRollupService.BuildInvoiceQuery(Matter, "sprk_matter"),
            FinanceRollupService.BuildPendingInvoiceQuery(Matter, "sprk_matter"),
        })
        {
            query.ColumnSet.AllColumns.Should().BeFalse();
            query.ColumnSet.Columns.Should().Contain(new[] { "sprk_approvedtotal", "sprk_totalamount", "sprk_invoicedate", "statuscode" });
        }
    }

    [Fact(DisplayName = "D-130: the stored figures (total, current month, prior month, timeline, count) all use the approved-else-billed amount")]
    public void D130_TheRollupFigures_UseTheApprovedTotal_ForTotalMonthsTimelineVelocityAndAverage()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        static Entity Dated(decimal billed, decimal? approved, DateTime date)
        {
            var e = new Entity("sprk_invoice", Guid.NewGuid()) { ["sprk_totalamount"] = new Money(billed), ["sprk_invoicedate"] = date };
            if (approved is { } a) e["sprk_approvedtotal"] = new Money(a);
            return e;
        }

        var invoices = new List<Entity>
        {
            Dated(1000m, 800m, new DateTime(2026, 10, 3)),     // this month, approved 800
            Dated(200m, null, new DateTime(2026, 10, 9)),      // this month, billed 200
            Dated(500m, 400m, new DateTime(2026, 9, 12)),      // last month, approved 400
            Dated(-100m, -100m, new DateTime(2026, 9, 20)),    // last month, credit memo
            Dated(300m, 0m, new DateTime(2026, 8, 1)),         // August, fully disallowed
        };

        var figures = FinanceRollupService.ComputeSpendFigures(invoices, now);

        figures.TotalSpend.Should().Be(1300m);                 // 800 + 200 + 400 - 100 + 0 (billed would be 1900)
        figures.CurrentMonthSpend.Should().Be(1000m);
        figures.PriorMonthSpend.Should().Be(300m);
        figures.InvoiceCount.Should().Be(5);
        figures.MonthlyBuckets["2026-10"].Should().Be(1000m);
        figures.MonthlyBuckets["2026-09"].Should().Be(300m);
        figures.MonthlyBuckets["2026-08"].Should().Be(0m);
        (figures.TotalSpend / figures.InvoiceCount).Should().Be(260m, "the stored average invoice is the total over the count");
        SpendSnapshotService.ComputeVelocityPct(figures.CurrentMonthSpend, figures.PriorMonthSpend)!.Value
            .Should().BeApproximately(233.33m, 0.01m, "(1000 - 300) / 300; on billed totals it would be a different number");
    }

    // ---- D-130 fail-closed: the live reader runs two queries per spend read and still never reads an unknown shape as zero ----

    private static EntityCollection Rows(object? parent, object? total) =>
        new() { Entities = { new Entity("sprk_invoice") { ["parent"] = new AliasedValue("sprk_invoice", "sprk_matter", parent), ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", total) } } };

    [Theory(DisplayName = "D-130 fail-closed: an unreadable row in ANY of the three spend reads throws; a half-read spend never comes back smaller")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task D130_AnUnreadableShapeInEitherPart_Throws_NotAPartialSum(int badCall)
    {
        var calls = 0;
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression _, CancellationToken _) =>
                Task.FromResult(calls++ == badCall ? Rows(Matter, "oops") : Rows(Matter, new Money(10m))));

        await FluentActions.Invoking(() => FinanceBaseValues.ReadAsync(service.Object, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: false, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*sum*unexpected type*String*");
        calls.Should().BeLessThanOrEqualTo(3);
    }

    [Theory(DisplayName = "D-130 fail-closed: a Dataverse failure in ANY of the three pending spend reads propagates, and pending is never read as zero")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task D130_ADataverseFailureInEitherPendingPart_Propagates(int failingCall)
    {
        var calls = 0;
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression _, CancellationToken _) =>
                calls++ == failingCall ? Task.FromException<EntityCollection>(new TimeoutException("dataverse")) : Task.FromResult(Rows(Matter, new Money(10m))));

        await FluentActions.Invoking(() => FinanceBaseValues.ReadPendingSpendAsync(service.Object, "sprk_matter", new[] { Matter }, CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();
    }

    [Fact(DisplayName = "D-130: the live spend read sends the two parts (billed-where-no-approved, approved) then the billed part again, plus one budget query when budget is needed")]
    public async Task D130_TheLiveRead_SendsTheTwoParts_ThenTheBilledRecheck_InTheDocumentedOrder()
    {
        var sent = new List<XElement>();
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression fetch, CancellationToken _) =>
            {
                sent.Add(XElement.Parse(fetch.Query));
                return Task.FromResult(new EntityCollection());
            });

        await FinanceBaseValues.ReadAsync(service.Object, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: true, CancellationToken.None);

        sent.Select(f => f.Element("entity")!.Attribute("name")!.Value).Should().Equal("sprk_invoice", "sprk_invoice", "sprk_invoice", "sprk_budget");
        sent.Take(3).Select(f => f.Descendants("attribute").Single(a => a.Attribute("aggregate") is not null).Attribute("name")!.Value)
            .Should().Equal("sprk_totalamount", "sprk_approvedtotal", "sprk_totalamount");
        sent[2].ToString(SaveOptions.DisableFormatting).Should().Be(sent[0].ToString(SaveOptions.DisableFormatting), "the re-check is the first billed query again");
    }

    // ---- K1 (ontology-r1 review): the two parts are not one snapshot; the billed re-read detects a racing write ----

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A service whose Nth invoice read is answered by <paramref name="answer"/> (call number, true when the query is the
    /// approved part); <paramref name="calls"/> records every read (true = approved part).</summary>
    private static IGenericEntityService Scripted(Func<int, bool, EntityCollection> answer, List<bool> calls)
    {
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression fetch, CancellationToken _) =>
            {
                var approvedPart = fetch.Query.Contains("not-null", StringComparison.Ordinal);
                calls.Add(approvedPart);
                return Task.FromResult(answer(calls.Count - 1, approvedPart));
            });
        return service.Object;
    }

    /// <summary>Per attempt: billed <paramref name="firstBilled"/> (call 0 of 3), approved 5, billed re-check <paramref name="recheckBilled"/>.</summary>
    private static EntityCollection Answer(bool approvedPart, int callNumber, decimal firstBilled, decimal recheckBilled) =>
        approvedPart ? Rows(Matter, new Money(5m)) : Rows(Matter, new Money(callNumber % 3 == 0 ? firstBilled : recheckBilled));

    [Fact(DisplayName = "K1: a stable world reads the billed part once more (three reads) and sums both parts; nothing is logged")]
    public async Task K1_AStableWorld_ReadsOnceMore_AndSumsCorrectly()
    {
        var calls = new List<bool>();
        var logger = new CapturingLogger();
        var service = Scripted((n, approved) => Answer(approved, n, 10m, 10m), calls);

        var counted = await FinanceBaseValues.ReadAsync(service, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: false, CancellationToken.None, logger);

        counted[Matter].Spend.Should().Be(15m);
        calls.Should().Equal(false, true, false);
        logger.Entries.Should().BeEmpty();
    }

    [Fact(DisplayName = "K1: a stable pending world reads three times and sums correctly")]
    public async Task K1_AStablePendingWorld_ReadsOnceMore_AndSumsCorrectly()
    {
        var calls = new List<bool>();
        var service = Scripted((n, approved) => Answer(approved, n, 10m, 10m), calls);

        (await FinanceBaseValues.ReadPendingSpendAsync(service, "sprk_matter", new[] { Matter }, CancellationToken.None))[Matter].Should().Be(15m);
        calls.Should().Equal(false, true, false);
    }

    [Fact(DisplayName = "K1 fail-closed: billed sums that differ between the billed reads on TWO consecutive attempts throw (counted)")]
    public async Task K1_BilledSumsThatChangeOnBothAttempts_Throw()
    {
        var calls = new List<bool>();
        var service = Scripted((n, approved) => Answer(approved, n, 10m, 99m), calls);

        await FluentActions.Invoking(() => FinanceBaseValues.ReadAsync(service, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: false, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Spend could not be read consistently*two consecutive attempts*");
        calls.Should().HaveCount(6, "two attempts of three reads, then it gives up");
    }

    [Fact(DisplayName = "K1 fail-closed: the same on the pending read")]
    public async Task K1_BilledSumsThatChangeOnBothAttempts_Throw_Pending()
    {
        var calls = new List<bool>();
        var service = Scripted((n, approved) => Answer(approved, n, 10m, 99m), calls);

        await FluentActions.Invoking(() => FinanceBaseValues.ReadPendingSpendAsync(service, "sprk_matter", new[] { Matter }, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Spend could not be read consistently*");
        calls.Should().HaveCount(6);
    }

    [Fact(DisplayName = "K1: a re-check that differs on the first attempt but is stable on the second succeeds with the second attempt's sums and logs one Warning (no values)")]
    public async Task K1_ADifferenceOnTheFirstAttemptOnly_RetriesOnce_AndLogsOneWarning()
    {
        var calls = new List<bool>();
        var logger = new CapturingLogger();
        // Attempt 1 (calls 0-2): billed 11, approved 5, re-check billed 77 (differs). Attempt 2 (calls 3-5): billed 23, approved 5, re-check 23.
        var service = Scripted((n, approved) => n switch
        {
            0 => Rows(Matter, new Money(11m)),
            2 => Rows(Matter, new Money(77m)),
            3 or 5 => Rows(Matter, new Money(23m)),
            _ => Rows(Matter, new Money(5m)),
        }, calls);

        var counted = await FinanceBaseValues.ReadAsync(service, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: false, CancellationToken.None, logger);

        counted[Matter].Spend.Should().Be(28m, "the second attempt's 23 + 5, not the first attempt's 11 + 5 or 77 + 5");
        calls.Should().HaveCount(6);
        var warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain("1 parent(s)").And.NotContain("77").And.NotContain("28").And.NotContain("23").And.NotContain("11");
    }

    [Fact(DisplayName = "K1: an approved total set between the billed read and the approved read (a credit memo) would have been counted twice; the re-read catches it and the retry returns the consistent sum")]
    public async Task K1_ARacingWrite_ThatSetsAnApprovedTotal_IsDetected_NotDoubleCounted()
    {
        var store = new Store();
        var creditMemo = store.Invoice(-100m, InvoiceStatusCodes.Approved);           // billed -100, no approved total yet
        store.Invoice(300m, InvoiceStatusCodes.Approved, approved: 300m);
        var inner = store.AsService();
        var raced = false;
        var service = new Mock<IGenericEntityService>();
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns(async (FetchExpression fetch, CancellationToken ct) =>
            {
                var result = await inner.RetrieveMultipleAsync(fetch, ct);
                if (!raced)
                {
                    raced = true;
                    creditMemo["sprk_approvedtotal"] = new Money(-100m);   // the totals service lands the approved total right after the billed read
                }

                return result;
            });
        var logger = new CapturingLogger();

        var spend = (await FinanceBaseValues.ReadAsync(service.Object, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: false, CancellationToken.None, logger))[Matter].Spend;

        spend.Should().Be(200m, "300 + (-100) once; a two-read order returned -100 (billed) + 200 (approved) = 100, a LOWER spend that lets a le / lt rule pass");
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    // ---- ontology-r1 re-check F3: a parent present ONLY in the re-check billed read (the other half of the key union) ----

    private static async Task<decimal> ReadSpendOf(IGenericEntityService service, bool pending, ILogger logger) =>
        pending
            ? (await FinanceBaseValues.ReadPendingSpendAsync(service, "sprk_matter", new[] { Matter }, CancellationToken.None, logger))[Matter]
            : (await FinanceBaseValues.ReadAsync(service, "sprk_matter", new[] { Matter }, needSpend: true, needBudget: false, CancellationToken.None, logger))[Matter].Spend;

    [Theory(DisplayName = "F3: a parent that appears ONLY in the re-check billed read is a mismatch; the retry that is stable returns the second attempt's sums and logs one Warning (counted and pending)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F3_AParentOnlyInTheRecheck_RetriesOnce_AndReturnsTheSecondAttemptsSums(bool pending)
    {
        var calls = new List<bool>();
        var logger = new CapturingLogger();
        // Attempt 1 (calls 0-2): billed EMPTY, approved 5, re-check billed 40 (the parent appears: an invoice entered the billed part). Attempt 2 (calls 3-5): billed 40, approved 5, re-check 40.
        var service = Scripted((n, approved) => n switch
        {
            0 => new EntityCollection(),
            2 or 3 or 5 => Rows(Matter, new Money(40m)),
            _ => Rows(Matter, new Money(5m)),
        }, calls);

        var spend = await ReadSpendOf(service, pending, logger);

        spend.Should().Be(45m, "the second attempt's 40 + 5; comparing only the first read's keys would accept attempt 1 and return 5");
        calls.Should().HaveCount(6, "the mismatch forces a second attempt of three reads");
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Theory(DisplayName = "F3 fail-closed: a parent that appears only in the re-check billed read on BOTH attempts throws (counted and pending)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F3_AParentOnlyInTheRecheck_OnBothAttempts_Throws(bool pending)
    {
        var calls = new List<bool>();
        var service = Scripted((n, approved) => n % 3 == 2 ? Rows(Matter, new Money(40m)) : new EntityCollection(), calls);

        await FluentActions.Invoking(() => ReadSpendOf(service, pending, new CapturingLogger()))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Spend could not be read consistently*two consecutive attempts*");
        calls.Should().HaveCount(6);
    }

    // ---- ontology-r1 re-check K1: DerivedBaseColumns must list every attribute the real queries touch ----

    /// <summary>Every (entity, attribute) a FetchXML names: attributes (selected, summed, grouped), condition attributes (resolved
    /// through <c>entityname</c> to a link alias), order attributes, and each link-entity's <c>from</c> (on the linked entity) and
    /// <c>to</c> (on its parent entity).</summary>
    private static IReadOnlyCollection<(string Entity, string Attribute)> ReferencedColumns(string fetchXml)
    {
        var root = XElement.Parse(fetchXml).Element("entity")!;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var link in root.Descendants("link-entity"))
        {
            names[link.Attribute("alias")?.Value ?? link.Attribute("name")!.Value] = link.Attribute("name")!.Value;
        }

        var found = new HashSet<(string, string)>();
        void Visit(XElement node)
        {
            var entity = node.Attribute("name")!.Value;
            foreach (var a in node.Elements("attribute").Concat(node.Elements("order")))
            {
                found.Add((entity, a.Attribute("name")?.Value ?? a.Attribute("attribute")!.Value));
            }

            foreach (var c in node.Elements("filter").Descendants("condition"))
            {
                var owner = c.Attribute("entityname") is { } alias ? names[alias.Value] : entity;
                found.Add((owner, c.Attribute("attribute")!.Value));
            }

            foreach (var link in node.Elements("link-entity"))
            {
                found.Add((link.Attribute("name")!.Value, link.Attribute("from")!.Value));
                found.Add((entity, link.Attribute("to")!.Value));
                Visit(link);
            }
        }

        Visit(root);
        return found;
    }

    private static IEnumerable<string> ColumnsMissingFromDerivedBaseColumns(string fetchXml) =>
        ReferencedColumns(fetchXml)
            .Where(c => c.Attribute != c.Entity + "id"
                && !FinanceBaseValues.DerivedBaseColumns.Contains((c.Entity, c.Attribute)))
            .Select(c => c.Entity + "." + c.Attribute)
            .OrderBy(c => c, StringComparer.Ordinal);

    [Fact(DisplayName = "K1 (re-check): every attribute the counted, pending and budget queries group, filter, sum or join on (matter and project) is in DerivedBaseColumns or is a primary key")]
    public void K1_EveryColumnTheQueriesTouch_IsInDerivedBaseColumns()
    {
        var queries = new List<string>();
        foreach (var parent in new[] { "sprk_matter", "sprk_project" })
        {
            foreach (var part in Enum.GetValues<FinanceBaseValues.SpendAmountPart>())
            {
                queries.Add(FinanceBaseValues.BuildGroupedSpendFetchXml(parent, part, new[] { Matter }));
                queries.Add(FinanceBaseValues.BuildGroupedPendingSpendFetchXml(parent, part, new[] { Matter }));
            }

            queries.Add(FinanceBaseValues.BuildGroupedBudgetFetchXml(parent, new[] { Matter }));
        }

        foreach (var fetch in queries)
        {
            ColumnsMissingFromDerivedBaseColumns(fetch).Should().BeEmpty($"every attribute of {fetch} must be guarded against field security");
        }

        // The parent lookups the queries group by are the ones that must be there (a secured group key reads null and the row is skipped).
        FinanceBaseValues.DerivedBaseColumns.Should().Contain(new (string, string)[]
        {
            ("sprk_invoice", "sprk_matter"), ("sprk_invoice", "sprk_project"), ("sprk_budget", "sprk_matter"), ("sprk_budget", "sprk_project"),
        });
    }

    [Fact(DisplayName = "K1 (re-check) control: the collector sees grouped, filtered, condition-on-link and join attributes, and flags one that is not listed")]
    public void K1_TheCollector_FindsEveryKindOfReference_AndFlagsAnUnlistedColumn()
    {
        const string fetch = "<fetch aggregate=\"true\"><entity name=\"sprk_invoice\">"
            + "<attribute name=\"sprk_totalamount\" alias=\"total\" aggregate=\"sum\" /><attribute name=\"sprk_unlisted_group\" alias=\"parent\" groupby=\"true\" />"
            + "<link-entity name=\"sprk_invoicesubmission\" from=\"sprk_invoice\" to=\"sprk_unlisted_join\" alias=\"held\"><filter><condition attribute=\"sprk_unlisted_link\" operator=\"eq\" value=\"1\" /></filter></link-entity>"
            + "<filter><condition attribute=\"sprk_unlisted_filter\" operator=\"null\" /><condition entityname=\"held\" attribute=\"sprk_invoicesubmissionid\" operator=\"null\" /></filter>"
            + "</entity></fetch>";

        ColumnsMissingFromDerivedBaseColumns(fetch).Should().Equal(
            "sprk_invoice.sprk_unlisted_filter", "sprk_invoice.sprk_unlisted_group", "sprk_invoice.sprk_unlisted_join", "sprk_invoicesubmission.sprk_unlisted_link");
    }

}
