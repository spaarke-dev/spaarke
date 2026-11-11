using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// Task 165b-4: one in-memory Dataverse for the spend readers (<see cref="FinanceBaseValues.ReadCountedInvoicesAsync"/>,
/// <see cref="FinanceBaseValues.ReadPendingSpendAsync"/>, <see cref="FinanceBaseValues.ReadAsync"/>, <see cref="FinanceRollupService"/> and the
/// finance tool): invoices, approval portions, Decision Records, invoice submissions and budgets. It answers the queries those readers send the
/// way Dataverse does (root conditions, an inner link to <c>sprk_invoice</c> with its link criteria, the column set, paging) and refuses any
/// shape it does not model, so a reader that gains a filter fails here instead of passing on a wrong sum.
/// </summary>
internal sealed class SpendWorld
{
    /// <summary>The writer identity (WhoAmI on the writer's connection): the writer test harness's own id.</summary>
    public static readonly Guid Writer = Tests.Services.Signals.DecisionRecordWriterTests.WriterUser;

    /// <summary>Task 165b-4 round 2: the writer-id provider the readers take, answering <see cref="Writer"/>.</summary>
    public static Task<Guid> WriterId(CancellationToken ct) => Task.FromResult(Writer);

    /// <summary>Another identity (a person or another application user).</summary>
    public static readonly Guid OtherUser = Guid.Parse("0bad0bad-0000-4000-8000-000000000001");

    private const string Approval = "sprk_invoiceapproval";
    private const string Record = "sprk_decisionrecord";
    private const string Submission = "sprk_invoicesubmission";

    public SpendWorld(List<Entity>? invoices = null, List<Entity>? budgets = null)
    {
        Invoices = invoices ?? new List<Entity>();
        Budgets = budgets ?? new List<Entity>();
    }

    public List<Entity> Invoices { get; }
    public List<Entity> Budgets { get; }
    public List<Entity> Portions { get; } = new();
    public List<Entity> Records { get; } = new();
    public List<Entity> Submissions { get; } = new();

    /// <summary>Every page read, in order (QueryExpression), and every FetchXML read.</summary>
    public List<QueryExpression> Reads { get; } = new();
    public List<string> Fetches { get; } = new();

    /// <summary>A fault the next matching read throws.</summary>
    public Func<QueryExpression, Exception?>? FaultOn { get; set; }

    /// <summary>The secured findings column reads back empty (the identity is not in its profile).</summary>
    public bool FindingsUnreadable { get; set; }

    /// <summary>Total reads (QueryExpression pages + FetchXML).</summary>
    public int ReadCount => Reads.Count + Fetches.Count;

    // ── Builders ──

    /// <summary>An invoice created by a submission (its id derives from the submission's, as the persister creates it).</summary>
    public Entity Invoice(string parentEntity, Guid parentId, decimal? billed, int status = InvoiceStatusCodes.Approved, decimal? approved = null)
    {
        var submissionId = Guid.NewGuid();
        var invoice = new Entity(FinanceBaseValues.InvoiceEntity, InvoiceSubmissionPersister.InvoiceIdFor(submissionId))
        {
            [parentEntity] = new EntityReference(parentEntity, parentId),
            ["statuscode"] = new OptionSetValue(status),
            ["sprk_invoicedate"] = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        if (billed is { } b) invoice["sprk_totalamount"] = new Money(b);
        if (approved is { } a) invoice["sprk_approvedtotal"] = new Money(a);
        _submissionOf[invoice.Id] = submissionId;
        Invoices.Add(invoice);
        return invoice;
    }

    /// <summary>An Approved / Sent to AP / Paid invoice with its portion and the writer's approve record under the approval's own key.</summary>
    public Entity Approved(string parentEntity, Guid parentId, decimal? billed, int status = InvoiceStatusCodes.Approved, decimal? approved = null)
    {
        var invoice = Invoice(parentEntity, parentId, billed, status, approved);
        ApproveRecord(invoice, KeyOf(invoice.Id, Portion(invoice)));
        return invoice;
    }

    /// <summary>An In Review invoice whose own submission is linked and not held (Accepted).</summary>
    public Entity Pending(string parentEntity, Guid parentId, decimal? billed, decimal? approved = null)
    {
        var invoice = Invoice(parentEntity, parentId, billed, InvoiceStatusCodes.InReview, approved);
        OwnSubmission(invoice);
        return invoice;
    }

    public Guid Portion(Entity invoice)
    {
        var portion = new Entity(Approval, Guid.NewGuid()) { ["sprk_invoice"] = new EntityReference(FinanceBaseValues.InvoiceEntity, invoice.Id) };
        Portions.Add(portion);
        return portion.Id;
    }

    public static Guid KeyOf(Guid invoiceId, Guid portionId) =>
        BillingDecisionKey.IdOf(FinanceBaseValues.InvoiceEntity, invoiceId, InvoiceApprovalService.DecisionIdFor(portionId));

    /// <summary>A Decision Record regarding the invoice. <paramref name="key"/> null writes no <c>sprk_steps</c> key.</summary>
    public Entity ApproveRecord(Entity invoice, Guid? key, Guid? createdBy = null, Guid? onBehalfOf = null, string actionCode = "approve-invoice", string? steps = null)
    {
        var record = new Entity(Record, Guid.NewGuid())
        {
            ["sprk_regardinginvoice"] = new EntityReference(FinanceBaseValues.InvoiceEntity, invoice.Id),
            ["sprk_actioncode"] = actionCode,
            ["sprk_steps"] = steps ?? (key is { } k ? new JsonObject { ["reviewId"] = k.ToString("D") }.ToJsonString() : "{}"),
            ["createdby"] = new EntityReference("systemuser", createdBy ?? Writer),
        };
        if (onBehalfOf is { } behalf) record["createdonbehalfby"] = new EntityReference("systemuser", behalf);
        Records.Add(record);
        return record;
    }

    /// <summary>The invoice's own submission (its id derives the invoice's), linked unless <paramref name="linked"/> is false.</summary>
    public Entity OwnSubmission(Entity invoice, string kind = "Accepted", bool overrideStamp = false, int status = (int)InvoiceSubmissionStatus.Accepted,
        string? reasonCode = null, bool linked = true)
    {
        var findings = new JsonObject { ["kind"] = kind, ["invoiceId"] = invoice.Id.ToString("D") };
        if (overrideStamp)
        {
            findings["override"] = new JsonObject
            {
                ["recordId"] = Guid.NewGuid().ToString("D"),
                ["bySystemUserId"] = OtherUser.ToString("D"),
                ["on"] = "2026-11-03T10:00:00Z",
                ["reason"] = "ok",
            };
        }

        var submission = new Entity(Submission, _submissionOf[invoice.Id])
        {
            ["sprk_processingstatus"] = new OptionSetValue(status),
            ["sprk_findingsjson"] = findings.ToJsonString(),
        };
        if (reasonCode is not null) submission["sprk_outcomereasoncode"] = reasonCode;
        if (linked) submission["sprk_invoice"] = new EntityReference(FinanceBaseValues.InvoiceEntity, invoice.Id);
        Submissions.RemoveAll(s => s.Id == submission.Id);
        Submissions.Add(submission);
        return submission;
    }

    public Entity Budget(string parentEntity, Guid parentId, decimal total)
    {
        var budget = new Entity(FinanceBaseValues.BudgetEntity, Guid.NewGuid())
        {
            [parentEntity] = new EntityReference(parentEntity, parentId),
            ["sprk_totalbudget"] = new Money(total),
        };
        Budgets.Add(budget);
        return budget;
    }

    private readonly Dictionary<Guid, Guid> _submissionOf = new();

    // ── The service ──

    /// <summary>An <see cref="IDataverseService"/> (also the <see cref="IGenericEntityService"/>) answered by this world.</summary>
    public Mock<IDataverseService> Service()
    {
        var service = new Mock<IDataverseService>(MockBehavior.Strict);
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) => Task.FromResult(Answer(q)));
        service.Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression f, CancellationToken _) => Task.FromResult(Answer(f)));
        return service;
    }

    public EntityCollection Answer(QueryExpression query)
    {
        Reads.Add(query);
        if (FaultOn?.Invoke(query) is { } fault) throw fault;
        var table = query.EntityName switch
        {
            FinanceBaseValues.InvoiceEntity => Invoices,
            FinanceBaseValues.BudgetEntity => Budgets,
            Approval => Portions,
            Record => Records,
            Submission => Submissions,
            _ => throw new NotSupportedException("SpendWorld does not model " + query.EntityName),
        };
        if (query.Criteria.FilterOperator != LogicalOperator.And || query.Criteria.Filters.Count > 0)
            throw new NotSupportedException("SpendWorld models one AND filter only.");

        var rows = table.Where(r => query.Criteria.Conditions.All(c => Holds(c, r)) && query.LinkEntities.All(l => LinkHolds(l, r)))
            .OrderBy(r => r.Id).ToList();
        var count = query.PageInfo?.Count > 0 ? query.PageInfo.Count : int.MaxValue;
        var page = Math.Max(1, query.PageInfo?.PageNumber ?? 1);
        var slice = (count == int.MaxValue ? rows : rows.Skip((page - 1) * count).Take(count)).ToList();
        var result = new EntityCollection(slice.Select(r => Project(r, query.ColumnSet)).ToList())
        {
            MoreRecords = count != int.MaxValue && rows.Count > page * count,
        };
        if (result.MoreRecords) result.PagingCookie = "page-" + page.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    /// <summary>The grouped budget aggregate (the only FetchXML a spend reader sends): the budget totals of the parents, summed per parent.</summary>
    public EntityCollection Answer(FetchExpression fetch)
    {
        Fetches.Add(fetch.Query);
        var entity = XElement.Parse(fetch.Query).Element("entity")!;
        if ((string?)entity.Attribute("name") != FinanceBaseValues.BudgetEntity)
            throw new NotSupportedException("SpendWorld answers FetchXML only for the grouped budget sum.");
        var lookup = (string)entity.Elements("attribute").Single(a => (string?)a.Attribute("groupby") == "true").Attribute("name")!;
        var conditions = entity.Element("filter")!.Elements("condition").ToList();
        if (conditions.Count != 1) throw new NotSupportedException("SpendWorld models the parent filter only on the budget aggregate.");
        var parents = conditions[0].Elements("value").Select(v => Guid.Parse(v.Value)).ToHashSet();
        var result = new EntityCollection();
        foreach (var byParent in Budgets.Where(b => b.GetAttributeValue<EntityReference>(lookup) is { } p && parents.Contains(p.Id))
                     .GroupBy(b => b.GetAttributeValue<EntityReference>(lookup)!.Id))
        {
            result.Entities.Add(new Entity(FinanceBaseValues.BudgetEntity)
            {
                ["parent"] = new AliasedValue(FinanceBaseValues.BudgetEntity, lookup, new EntityReference(lookup, byParent.Key)),
                ["total"] = new AliasedValue(FinanceBaseValues.BudgetEntity, "sprk_totalbudget",
                    new Money(byParent.Sum(b => b.GetAttributeValue<Money>("sprk_totalbudget")?.Value ?? 0m))),
            });
        }

        return result;
    }

    private bool LinkHolds(LinkEntity link, Entity row)
    {
        if (link.JoinOperator != JoinOperator.Inner || link.LinkToEntityName != FinanceBaseValues.InvoiceEntity || link.LinkToAttributeName != "sprk_invoiceid"
            || link.LinkEntities.Count > 0 || link.LinkCriteria.Filters.Count > 0 || link.LinkCriteria.FilterOperator != LogicalOperator.And)
            throw new NotSupportedException($"SpendWorld models only an inner link to sprk_invoice on its key, not {link.LinkToEntityName}.{link.LinkToAttributeName} ({link.JoinOperator}).");
        if (row.GetAttributeValue<EntityReference>(link.LinkFromAttributeName)?.Id is not { } invoiceId) return false;
        var invoice = Invoices.FirstOrDefault(i => i.Id == invoiceId);
        return invoice is not null && link.LinkCriteria.Conditions.All(c => Holds(c, invoice));
    }

    private static bool Holds(ConditionExpression c, Entity row)
    {
        if (!string.IsNullOrEmpty(c.EntityName)) throw new NotSupportedException("SpendWorld does not model a condition on a linked alias.");
        var actual = Unwrap(c.AttributeName == row.LogicalName + "id" ? row.Id : row.Contains(c.AttributeName) ? row[c.AttributeName] : null);
        return c.Operator switch
        {
            ConditionOperator.Equal => actual is not null && Equals(actual, Unwrap(c.Values.Single())),
            ConditionOperator.In => actual is not null && c.Values.Select(Unwrap).Contains(actual),
            ConditionOperator.Null => actual is null,
            ConditionOperator.NotNull => actual is not null,
            _ => throw new NotSupportedException("SpendWorld does not model " + c.Operator),
        };
    }

    private static object? Unwrap(object? value) => value switch
    {
        EntityReference r => r.Id,
        OptionSetValue o => o.Value,
        Money m => m.Value,
        _ => value,
    };

    private Entity Project(Entity row, ColumnSet columns)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var attribute in row.Attributes)
        {
            if (!columns.AllColumns && !columns.Columns.Contains(attribute.Key)) continue;
            if (FindingsUnreadable && row.LogicalName == Submission && attribute.Key == "sprk_findingsjson") continue;
            copy[attribute.Key] = attribute.Value;
        }

        return copy;
    }
}
