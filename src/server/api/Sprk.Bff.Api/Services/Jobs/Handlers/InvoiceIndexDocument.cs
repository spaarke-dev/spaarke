using System.Globalization;
using System.Text;
using Azure.Search.Documents.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// The search document of one invoice in <c>spaarke-invoices-index</c>, built from the invoice's structured records (ebilling-r1 task 152,
/// FR-43, D-44): one document per invoice (<c>id</c> = the invoice id), never from document text and never from a submission. Pure: the
/// handler reads the rows, this type maps them; the same rows always give the same document.
/// </summary>
/// <remarks>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceIndexingJobHandler"/> built its document inline
/// from placeholder loaders. Extension: the mapping is now a pure function of two row sets and the handler keeps only the job plumbing, so
/// the document shape is testable without a search client. Cost of doing nothing: FR-43's firm / core record / UTBMS filters have no
/// tested producer.</para>
/// <para>Logs never carry the narrative (ADR-015); <see cref="InvoiceIndexModel.Content"/> is for the index only.</para>
/// </remarks>
internal static class InvoiceIndexDocument
{
    /// <summary>The <c>documentType</c> value of every document in the invoice index.</summary>
    internal const string DocumentType = "invoice";

    /// <summary>
    /// Characters of <see cref="InvoiceIndexModel.Content"/> sent to the embedding model: about 6,000 tokens at 4 characters a token, under the
    /// model's 8,191-token input limit, so a very long invoice is embedded from its header and first lines rather than failing the job.
    /// </summary>
    internal const int EmbeddingInputMaxChars = 24_000;

    internal const string StatusCodeColumn = "statuscode";
    internal const string InvoiceName = "sprk_name";
    internal const string InvoiceNumber = "sprk_invoicenumber";
    internal const string InvoiceDate = "sprk_invoicedate";
    internal const string InvoiceDocumentLookup = "sprk_document";
    internal const string InvoiceMatter = "sprk_matter";
    internal const string InvoiceProject = "sprk_project";
    internal const string InvoiceWorkAssignment = "sprk_regardingworkassignment";

    internal const string LineTaskCode = "sprk_taskcode";
    internal const string LineActivityCode = "sprk_activitycode";
    internal const string LineExpenseCode = "sprk_expensecode";

    /// <summary>The invoice columns the document is built from.</summary>
    internal static readonly string[] InvoiceColumns =
    [
        InvoiceName, InvoiceNumber, InvoiceReviewService.InvTrackingNumber, InvoiceDate,
        InvoiceSubmissionPersister.InvoiceBillingPeriodStart, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd,
        InvoiceSubmissionPersister.InvoiceVendorOrg, InvoiceSubmissionPersister.InvoiceCurrency, InvoiceDocumentLookup,
        InvoiceMatter, InvoiceProject, InvoiceWorkAssignment,
        InvoiceTotalsService.InvoiceBilledTotal, InvoiceTotalsService.InvoiceApprovedTotal,
        StatusCodeColumn,
    ];

    /// <summary>The line columns the document is built from.</summary>
    internal static readonly string[] LineColumns =
    [
        InvoiceTotalsService.LineCostType, InvoiceTotalsService.LineState, InvoiceSubmissionPersister.LineSequence,
        InvoiceSubmissionPersister.LineEventDate, InvoiceSubmissionPersister.LineTimekeeper, InvoiceSubmissionPersister.LineResolvedTimekeeper,
        InvoiceSubmissionPersister.LineDescription, LineTaskCode, LineActivityCode, LineExpenseCode,
        InvoiceSubmissionPersister.LineTaskCodeAsBilled, InvoiceSubmissionPersister.LineActivityCodeAsBilled,
        InvoiceSubmissionPersister.LineExpenseCodeAsBilled,
    ];

    /// <summary>Maps the invoice row and its lines (any order; Adjustment lines are skipped) to the document's facts.</summary>
    internal static InvoiceIndexModel Read(Entity invoice, IReadOnlyList<Entity> lines)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(lines);

        var statusCode = invoice.GetAttributeValue<OptionSetValue>(StatusCodeColumn)?.Value;
        var status = statusCode is { } code && InvoiceStatusCodes.TryFromStatusCode(code, out var known) ? known.ToString() : "Unknown";

        var (coreType, coreRef) = CoreRecordOf(invoice);
        var firm = invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg);

        var timekeeperIds = new SortedSet<string>(StringComparer.Ordinal);
        var timekeeperNames = new SortedSet<string>(StringComparer.Ordinal);
        var taskCodes = new SortedSet<string>(StringComparer.Ordinal);
        var activityCodes = new SortedSet<string>(StringComparer.Ordinal);
        var expenseCodes = new SortedSet<string>(StringComparer.Ordinal);
        var lineTexts = new List<string>();

        foreach (var line in lines
                     .Where(IsFeeOrExpense)
                     .OrderBy(l => l.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? int.MaxValue)
                     .ThenBy(l => l.Id))
        {
            var resolved = line.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper);
            if (resolved is not null && resolved.Id != Guid.Empty)
            {
                timekeeperIds.Add(IdText(resolved.Id));
            }

            var timekeeperName = Clean(resolved?.Name) ?? Clean(line.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper));
            if (timekeeperName is not null)
            {
                timekeeperNames.Add(timekeeperName);
            }

            var task = CodeOf(line, LineTaskCode, InvoiceSubmissionPersister.LineTaskCodeAsBilled);
            var activity = CodeOf(line, LineActivityCode, InvoiceSubmissionPersister.LineActivityCodeAsBilled);
            var expense = CodeOf(line, LineExpenseCode, InvoiceSubmissionPersister.LineExpenseCodeAsBilled);
            AddIfAny(taskCodes, task);
            AddIfAny(activityCodes, activity);
            AddIfAny(expenseCodes, expense);

            lineTexts.Add(LineText(line, timekeeperName, task, activity, expense));
        }

        var model = new InvoiceIndexModel
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = Clean(invoice.GetAttributeValue<string>(InvoiceNumber)),
            TrackingNumber = Clean(invoice.GetAttributeValue<string>(InvoiceReviewService.InvTrackingNumber)),
            InvoiceDate = DateOf(invoice, InvoiceDate),
            BillingPeriodStart = DateOf(invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodStart),
            BillingPeriodEnd = DateOf(invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd),
            Currency = Clean(invoice.GetAttributeValue<string>(InvoiceSubmissionPersister.InvoiceCurrency)),
            BilledTotal = MoneyOf(invoice, InvoiceTotalsService.InvoiceBilledTotal),
            ApprovedTotal = MoneyOf(invoice, InvoiceTotalsService.InvoiceApprovedTotal),
            StatusCode = statusCode,
            Status = status,
            FirmId = firm is { } f && f.Id != Guid.Empty ? f.Id : null,
            FirmName = Clean(firm?.Name),
            CoreRecordType = coreType,
            CoreRecordId = coreRef is { } c ? c.Id : null,
            CoreRecordName = Clean(coreRef?.Name),
            DocumentId = invoice.GetAttributeValue<EntityReference>(InvoiceDocumentLookup) is { } d && d.Id != Guid.Empty ? d.Id : null,
            TimekeeperIds = timekeeperIds.ToList(),
            TimekeeperNames = timekeeperNames.ToList(),
            TaskCodes = taskCodes.ToList(),
            ActivityCodes = activityCodes.ToList(),
            ExpenseCodes = expenseCodes.ToList(),
        };

        return model with { Content = ComposeContent(model, lineTexts) };
    }

    /// <summary>
    /// The search document. The vector is set only when there is one: a Merge keeps the stored vector of an omitted field, so a re-index that
    /// could not embed (AI down, AI off) never erases the vector an earlier run wrote.
    /// </summary>
    internal static SearchDocument ToSearchDocument(InvoiceIndexModel model, string tenantId, ReadOnlyMemory<float> embedding, DateTimeOffset indexedAt)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var doc = new SearchDocument
        {
            ["id"] = IdText(model.InvoiceId),
            ["content"] = model.Content,
            ["chunkIndex"] = 0,
            ["invoiceId"] = IdText(model.InvoiceId),
            ["documentId"] = model.DocumentId is { } document ? IdText(document) : null,

            // The firm (D-70: sprk_vendororg) and the core record (FR-05). matterId / projectId stay filled for those two types: the
            // invoice search endpoint filters on matterId.
            ["vendorOrgId"] = model.FirmId is { } firm ? IdText(firm) : null,
            ["vendorName"] = model.FirmName,
            ["coreRecordType"] = model.CoreRecordType,
            ["coreRecordId"] = model.CoreRecordId is { } core ? IdText(core) : null,
            ["coreRecordName"] = model.CoreRecordName,
            ["matterId"] = model.CoreRecordType == InvoiceCoreRecordTypes.Matter && model.CoreRecordId is { } matter ? IdText(matter) : null,
            ["projectId"] = model.CoreRecordType == InvoiceCoreRecordTypes.Project && model.CoreRecordId is { } project ? IdText(project) : null,

            ["timekeeperIds"] = model.TimekeeperIds.ToArray(),
            ["timekeeperNames"] = model.TimekeeperNames.ToArray(),
            ["taskCodes"] = model.TaskCodes.ToArray(),
            ["activityCodes"] = model.ActivityCodes.ToArray(),
            ["expenseCodes"] = model.ExpenseCodes.ToArray(),

            ["invoiceNumber"] = model.InvoiceNumber,
            ["trackingNumber"] = model.TrackingNumber,
            ["invoiceDate"] = model.InvoiceDate,
            ["billingPeriodStart"] = model.BillingPeriodStart,
            ["billingPeriodEnd"] = model.BillingPeriodEnd,
            ["totalAmount"] = model.BilledTotal is { } billed ? (double)billed : null,
            ["approvedTotal"] = model.ApprovedTotal is { } approved ? (double)approved : null,
            ["currency"] = model.Currency,
            ["statusCode"] = model.StatusCode,
            ["status"] = model.Status,

            ["documentType"] = DocumentType,
            ["tenantId"] = tenantId,
            ["indexedAt"] = indexedAt,
        };

        if (embedding.Length > 0)
        {
            doc["contentVector"] = embedding.ToArray();
        }

        return doc;
    }

    /// <summary>The text the embedding model reads: the head of <see cref="InvoiceIndexModel.Content"/>, capped at <see cref="EmbeddingInputMaxChars"/>.</summary>
    internal static string EmbeddingInput(InvoiceIndexModel model) =>
        model.Content.Length <= EmbeddingInputMaxChars ? model.Content : model.Content[..EmbeddingInputMaxChars];

    /// <summary>The lowercase hyphenated id form every index field uses (Edm.String filters are case-sensitive; the rag-indexing pattern).</summary>
    internal static string IdText(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static string ComposeContent(InvoiceIndexModel m, IReadOnlyList<string> lineTexts)
    {
        var text = new StringBuilder();
        text.Append("Invoice ").Append(m.InvoiceNumber ?? "(no number)");
        if (m.TrackingNumber is not null)
        {
            text.Append(" (tracking ").Append(m.TrackingNumber).Append(')');
        }

        if (m.FirmName is not null)
        {
            text.Append(" from ").Append(m.FirmName);
        }

        if (m.CoreRecordType is not null)
        {
            text.Append(" for ").Append(InvoiceCoreRecordTypes.Label(m.CoreRecordType));
            if (m.CoreRecordName is not null)
            {
                text.Append(' ').Append(m.CoreRecordName);
            }
        }

        text.Append(". Status: ").Append(m.Status).Append('.');
        AppendDate(text, " Invoice date ", m.InvoiceDate);
        if (m.BillingPeriodStart is not null || m.BillingPeriodEnd is not null)
        {
            text.Append(" Billing period ").Append(DateText(m.BillingPeriodStart)).Append(" to ").Append(DateText(m.BillingPeriodEnd)).Append('.');
        }

        if (m.BilledTotal is { } billed)
        {
            text.Append(" Billed ").Append(MoneyText(billed)).Append(m.Currency is null ? string.Empty : " " + m.Currency).Append('.');
        }

        if (m.ApprovedTotal is { } approved)
        {
            text.Append(" Approved ").Append(MoneyText(approved)).Append('.');
        }

        AppendList(text, "Timekeepers", m.TimekeeperNames);
        AppendList(text, "Task codes", m.TaskCodes);
        AppendList(text, "Activity codes", m.ActivityCodes);
        AppendList(text, "Expense codes", m.ExpenseCodes);

        foreach (var line in lineTexts)
        {
            text.Append('\n').Append(line);
        }

        return text.ToString();
    }

    private static string LineText(Entity line, string? timekeeper, string? task, string? activity, string? expense)
    {
        var parts = new List<string>();
        if (DateOf(line, InvoiceSubmissionPersister.LineEventDate) is { } at)
        {
            parts.Add(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        AddIfAny(parts, timekeeper);
        var codes = string.Join(' ', new[] { task, activity, expense }.Where(c => c is not null));
        AddIfAny(parts, codes.Length == 0 ? null : codes);

        var head = string.Join(" | ", parts);
        var narrative = Clean(line.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription));
        return narrative is null ? head : head.Length == 0 ? narrative : $"{head}: {narrative}";
    }

    private static bool IsFeeOrExpense(Entity line)
    {
        var type = line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value;
        return type is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense;
    }

    private static (string? Type, EntityReference? Reference) CoreRecordOf(Entity invoice)
    {
        foreach (var (column, type) in new[]
                 {
                     (InvoiceMatter, InvoiceCoreRecordTypes.Matter),
                     (InvoiceProject, InvoiceCoreRecordTypes.Project),
                     (InvoiceWorkAssignment, InvoiceCoreRecordTypes.WorkAssignment),
                 })
        {
            if (invoice.GetAttributeValue<EntityReference>(column) is { } reference && reference.Id != Guid.Empty)
            {
                return (type, reference);
            }
        }

        return (null, null);
    }

    /// <summary>The line's code: the resolved code row's code (its primary name), else the code as billed.</summary>
    private static string? CodeOf(Entity line, string lookupColumn, string asBilledColumn) =>
        Clean(line.GetAttributeValue<EntityReference>(lookupColumn)?.Name) ?? Clean(line.GetAttributeValue<string>(asBilledColumn));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIfAny(ISet<string> set, string? value)
    {
        if (value is not null)
        {
            set.Add(value);
        }
    }

    private static void AddIfAny(ICollection<string> list, string? value)
    {
        if (value is not null)
        {
            list.Add(value);
        }
    }

    private static void AppendList(StringBuilder text, string label, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            text.Append(' ').Append(label).Append(": ").Append(string.Join(", ", values)).Append('.');
        }
    }

    private static void AppendDate(StringBuilder text, string label, DateTimeOffset? value)
    {
        if (value is { } date)
        {
            text.Append(label).Append(DateText(date)).Append('.');
        }
    }

    private static string DateText(DateTimeOffset? value) => value is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "?";

    private static string MoneyText(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static DateTimeOffset? DateOf(Entity row, string column) =>
        row.GetAttributeValue<DateTime?>(column) is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;

    private static decimal? MoneyOf(Entity row, string column) => row.GetAttributeValue<Money>(column)?.Value;

    /// <summary>The one invoice-by-id query: the id filter, one row.</summary>
    internal static QueryExpression InvoiceQuery(Guid invoiceId)
    {
        var query = new QueryExpression(InvoiceTotalsService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceTotalsService.InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
        return query;
    }

    /// <summary>The invoice's lines, in sequence order, a removed (deactivated) Adjustment line excluded as every other reader does.</summary>
    internal static QueryExpression LineQuery(Guid invoiceId)
    {
        var query = new QueryExpression(InvoiceTotalsService.LineEntity)
        {
            ColumnSet = new ColumnSet(LineColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        query.Criteria.AddCondition(InvoiceTotalsService.LineInvoiceLookup, ConditionOperator.Equal, invoiceId);
        InvoiceTotalsService.ExcludeInactiveAdjustments(query);
        query.AddOrder(InvoiceSubmissionPersister.LineSequence, OrderType.Ascending);
        query.AddOrder($"{InvoiceTotalsService.LineEntity}id", OrderType.Ascending);
        return query;
    }
}

/// <summary>The core-record type names of the index and the review read model (one list: matter, project, work assignment).</summary>
internal static class InvoiceCoreRecordTypes
{
    internal const string Matter = "matter";
    internal const string Project = "project";
    internal const string WorkAssignment = "workAssignment";

    internal static string Label(string type) => type switch
    {
        Matter => "matter",
        Project => "project",
        WorkAssignment => "work assignment",
        _ => type,
    };
}

/// <summary>The facts one invoice document is made of (nothing here is read from document text or a submission).</summary>
internal sealed record InvoiceIndexModel
{
    public Guid InvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? TrackingNumber { get; init; }
    public DateTimeOffset? InvoiceDate { get; init; }
    public DateTimeOffset? BillingPeriodStart { get; init; }
    public DateTimeOffset? BillingPeriodEnd { get; init; }
    public string? Currency { get; init; }
    public decimal? BilledTotal { get; init; }
    public decimal? ApprovedTotal { get; init; }
    public int? StatusCode { get; init; }
    public string Status { get; init; } = "Unknown";
    public Guid? FirmId { get; init; }
    public string? FirmName { get; init; }
    public string? CoreRecordType { get; init; }
    public Guid? CoreRecordId { get; init; }
    public string? CoreRecordName { get; init; }
    public Guid? DocumentId { get; init; }
    public IReadOnlyList<string> TimekeeperIds { get; init; } = [];
    public IReadOnlyList<string> TimekeeperNames { get; init; } = [];
    public IReadOnlyList<string> TaskCodes { get; init; } = [];
    public IReadOnlyList<string> ActivityCodes { get; init; } = [];
    public IReadOnlyList<string> ExpenseCodes { get; init; } = [];

    /// <summary>The searchable text: header facts, then one line per fee / expense line with its narrative.</summary>
    public string Content { get; init; } = string.Empty;
}
