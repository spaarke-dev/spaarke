using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// <see cref="InvoiceIndexingJobHandler"/> and <see cref="InvoiceIndexDocument"/> (ebilling-r1 task 152, FR-43, D-44, NFR-03): the invoice
/// index is built from the invoice's structured records, per invoice, and is found by firm, core record and UTBMS code.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the document is the contract between the BFF and the search; a field written under a name the index does not
/// have, or a missing tenant, is a silent search failure. The Dataverse boundary is a Moq double of <see cref="IGenericEntityService"/>
/// that answers ONLY the invoice and line tables (any other table throws, which proves the handler reads no document and no submission);
/// the search service is a Moq double of the SDK client.
/// </remarks>
public sealed class InvoiceIndexingJobHandlerTests
{
    private static readonly Guid InvoiceId = Guid.Parse("15200000-0000-4000-8000-000000000001");
    private static readonly Guid FirmId = Guid.Parse("15200000-0000-4000-8000-0000000000f1");
    private static readonly Guid ProjectId = Guid.Parse("15200000-0000-4000-8000-0000000000a2");
    private static readonly Guid MatterId = Guid.Parse("15200000-0000-4000-8000-0000000000a1");
    private static readonly Guid WorkAssignmentId = Guid.Parse("15200000-0000-4000-8000-0000000000a3");
    private static readonly Guid TimekeeperA = Guid.Parse("15200000-0000-4000-8000-0000000000c1");
    private static readonly Guid DocumentId = Guid.Parse("15200000-0000-4000-8000-0000000000d1");
    private const string Tenant = "tenant-152";

    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    // ── Rows ──

    private static Entity Invoice(
        int statusCode = InvoiceStatusCodes.InReview,
        string coreColumn = "sprk_project",
        Guid? coreId = null,
        string coreName = "Project Orion",
        bool withDocument = true)
    {
        var invoice = new Entity("sprk_invoice", InvoiceId)
        {
            ["sprk_name"] = "INV-100.1",
            ["sprk_invoicenumber"] = "INV-100",
            ["sprk_trackingnumber"] = "INV-100.1",
            ["sprk_invoicedate"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_billingperiodstart"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_billingperiodend"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId) { Name = "Acme Counsel LLP" },
            ["sprk_currency"] = "USD",
            ["sprk_totalamount"] = new Money(1250.50m),
            ["sprk_approvedtotal"] = new Money(1000m),
            ["statuscode"] = new OptionSetValue(statusCode),
            [coreColumn] = new EntityReference(coreColumn.Replace("regardingworkassignment", "workassignment"), coreId ?? ProjectId) { Name = coreName },
        };
        if (withDocument)
        {
            invoice["sprk_document"] = new EntityReference("sprk_document", DocumentId);
        }

        return invoice;
    }

    private static Entity Line(
        int sequence,
        int costType = (int)InvoiceLineCostType.Fee,
        string? description = null,
        Guid? timekeeper = null,
        string? timekeeperName = null,
        string? billedTimekeeper = null,
        string? task = null,
        string? activity = null,
        string? expense = null,
        string? taskAsBilled = null,
        string? activityAsBilled = null,
        string? expenseAsBilled = null)
    {
        var line = new Entity("sprk_billingevent", Guid.Parse($"15200000-0000-4000-8000-00000000{sequence:D4}"))
        {
            ["sprk_costtype"] = new OptionSetValue(costType),
            ["sprk_linesequence"] = sequence,
            ["sprk_eventdate"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(sequence),
            ["sprk_description"] = description,
            ["sprk_timekeeper"] = billedTimekeeper,
            ["sprk_taskcodeasbilled"] = taskAsBilled,
            ["sprk_activitycodeasbilled"] = activityAsBilled,
            ["sprk_expensecodeasbilled"] = expenseAsBilled,
        };
        if (timekeeper is { } id)
        {
            line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", id) { Name = timekeeperName };
        }

        if (task is not null)
        {
            line["sprk_taskcode"] = new EntityReference("sprk_billingcode", Guid.NewGuid()) { Name = task };
        }

        if (activity is not null)
        {
            line["sprk_activitycode"] = new EntityReference("sprk_billingcode", Guid.NewGuid()) { Name = activity };
        }

        if (expense is not null)
        {
            line["sprk_expensecode"] = new EntityReference("sprk_billingcode", Guid.NewGuid()) { Name = expense };
        }

        return line;
    }

    private static List<Entity> StandardLines() =>
    [
        Line(1, description: "Review the merger agreement", timekeeper: TimekeeperA, timekeeperName: "Jane Partner", task: "L110", activity: "A104"),
        Line(2, description: "Call with client", billedTimekeeper: "J. Associate", taskAsBilled: "L120", activityAsBilled: "A106"),
        Line(3, costType: (int)InvoiceLineCostType.Expense, description: "Court filing fee", expense: "E112"),
        Line(4, costType: (int)InvoiceLineCostType.Adjustment, description: "Courtesy discount"),
    ];

    // ── Harness ──

    private sealed class Harness
    {
        public Entity? InvoiceRow { get; set; } = Invoice();
        public List<Entity> Lines { get; set; } = StandardLines();
        public Dictionary<string, string?> Settings { get; } = new() { ["AzureAd:TenantId"] = Tenant };
        public IInvoiceAi? Ai { get; set; }
        public bool UseAi { get; set; } = true;
        public List<string> QueriedTables { get; } = [];
        public List<IndexDocumentsBatch<SearchDocument>> Batches { get; } = [];
        public IndexingResult? Result { get; set; } = SearchModelFactory.IndexingResult("k", null, true, 201);
        public Exception? SearchFault { get; set; }
        public FakeTimeProvider Clock { get; } = new(Now);
        public List<string> EmbeddingInputs { get; } = [];

        public Harness()
        {
            var ai = new Mock<IInvoiceAi>();
            ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Returns((string text, string? _, int? _, CancellationToken _) =>
                {
                    EmbeddingInputs.Add(text);
                    return Task.FromResult<ReadOnlyMemory<float>>(new float[] { 0.25f, 0.5f, 0.75f });
                });
            Ai = ai.Object;
        }

        public InvoiceIndexingJobHandler Build()
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) =>
                {
                    QueriedTables.Add(q.EntityName);
                    return Task.FromResult(q.EntityName switch
                    {
                        "sprk_invoice" => new EntityCollection(InvoiceRow is null ? new List<Entity>() : new List<Entity> { InvoiceRow }),
                        "sprk_billingevent" => new EntityCollection(Lines.ToList()),
                        _ => throw new InvalidOperationException($"the handler read {q.EntityName}"),
                    });
                });

            var searchClient = new Mock<SearchClient>();
            searchClient
                .Setup(c => c.IndexDocumentsAsync(It.IsAny<IndexDocumentsBatch<SearchDocument>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IndexDocumentsBatch<SearchDocument> batch, IndexDocumentsOptions _, CancellationToken _) =>
                {
                    if (SearchFault is { } fault)
                    {
                        throw fault;
                    }

                    Batches.Add(batch);
                    return Task.FromResult(Response.FromValue(SearchModelFactory.IndexDocumentsResult(Result is null ? Array.Empty<IndexingResult>() : new[] { Result }), Mock.Of<Response>()));
                });
            var indexClient = new Mock<SearchIndexClient>();
            indexClient.Setup(c => c.GetSearchClient("spaarke-invoices-index")).Returns(searchClient.Object);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
            return new InvoiceIndexingJobHandler(
                indexClient.Object, entities.Object, configuration, NullLogger<InvoiceIndexingJobHandler>.Instance,
                UseAi ? Ai : null, Clock);
        }

        public SearchDocument OnlyDocument()
        {
            var action = Batches.Should().ContainSingle().Subject.Actions.Should().ContainSingle().Subject;
            action.ActionType.Should().Be(IndexActionType.MergeOrUpload);
            return action.Document;
        }
    }

    private static JobContract Job(string change = "accepted") => InvoiceIndexingJobHandler.BuildJob(InvoiceId, change, "corr-152");

    private static JobContract JobWithPayload(string json) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = InvoiceIndexingJobHandler.JobTypeName,
        SubjectId = InvoiceId.ToString(),
        CorrelationId = "corr",
        IdempotencyKey = "k",
        Attempt = 1,
        MaxAttempts = 3,
        CreatedAt = Now,
        Payload = JsonDocument.Parse(json),
    };

    // ── The document is built from structured records ──

    [Fact(DisplayName = "152 FR-43: the document carries firm, core record type and id, timekeepers, task / activity / expense codes, totals, status, tenant and narratives, all from the records")]
    public async Task Document_IsBuiltFromTheStructuredRecords()
    {
        var h = new Harness();

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var doc = h.OnlyDocument();
        doc["id"].Should().Be(InvoiceId.ToString("D"), "one document per invoice: the key is the invoice id");
        doc["invoiceId"].Should().Be(InvoiceId.ToString("D"));
        doc["tenantId"].Should().Be(Tenant);
        doc["documentType"].Should().Be("invoice");
        doc["indexedAt"].Should().Be(Now);
        doc["vendorOrgId"].Should().Be(FirmId.ToString("D"));
        doc["vendorName"].Should().Be("Acme Counsel LLP");
        doc["coreRecordType"].Should().Be("project", "a Project invoice is a project, not a matter");
        doc["coreRecordId"].Should().Be(ProjectId.ToString("D"));
        doc["coreRecordName"].Should().Be("Project Orion");
        doc["projectId"].Should().Be(ProjectId.ToString("D"));
        doc["matterId"].Should().BeNull();
        ((string[])doc["timekeeperIds"]).Should().Equal(TimekeeperA.ToString("D"));
        ((string[])doc["timekeeperNames"]).Should().BeEquivalentTo("Jane Partner", "J. Associate");
        ((string[])doc["taskCodes"]).Should().Equal("L110", "L120");
        ((string[])doc["activityCodes"]).Should().Equal("A104", "A106");
        ((string[])doc["expenseCodes"]).Should().Equal("E112");
        doc["totalAmount"].Should().Be(1250.5d);
        doc["approvedTotal"].Should().Be(1000d);
        doc["currency"].Should().Be("USD");
        doc["statusCode"].Should().Be(InvoiceStatusCodes.InReview);
        doc["status"].Should().Be("InReview");
        doc["invoiceNumber"].Should().Be("INV-100");
        doc["trackingNumber"].Should().Be("INV-100.1");
        doc["invoiceDate"].Should().Be(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        doc["billingPeriodStart"].Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        doc["billingPeriodEnd"].Should().Be(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        doc["documentId"].Should().Be(DocumentId.ToString("D"));
        var content = (string)doc["content"];
        content.Should().Contain("Acme Counsel LLP").And.Contain("project Project Orion").And.Contain("Status: InReview")
            .And.Contain("Review the merger agreement").And.Contain("Call with client").And.Contain("Court filing fee")
            .And.Contain("L110").And.Contain("E112").And.Contain("Jane Partner");
        content.Should().NotContain("Courtesy discount", "an Adjustment line is not a billed line");
        doc["contentVector"].Should().BeAssignableTo<float[]>().Which.Should().Equal(0.25f, 0.5f, 0.75f);
    }

    [Theory(DisplayName = "152: the core record type follows the invoice's own lookup (matter, project, work assignment); matterId / projectId fill only for those two")]
    [InlineData("sprk_matter", "matter", true, false)]
    [InlineData("sprk_project", "project", false, true)]
    [InlineData("sprk_regardingworkassignment", "workAssignment", false, false)]
    public async Task CoreRecordType_FollowsTheLookup(string column, string type, bool matterFilled, bool projectFilled)
    {
        var h = new Harness { InvoiceRow = Invoice(coreColumn: column, coreId: MatterId, coreName: "Core Name") };

        (await h.Build().ProcessAsync(Job(), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        var doc = h.OnlyDocument();
        doc["coreRecordType"].Should().Be(type);
        doc["coreRecordId"].Should().Be(MatterId.ToString("D"));
        (doc["matterId"] is not null).Should().Be(matterFilled);
        (doc["projectId"] is not null).Should().Be(projectFilled);
    }

    [Fact(DisplayName = "152: a line with no resolved code falls back to the code as billed; codes and timekeepers are distinct and ordered; a line without any adds nothing")]
    public async Task Codes_FallBackToAsBilled_AreDistinctAndOrdered()
    {
        var h = new Harness
        {
            Lines =
            [
                Line(1, task: "L120", activity: "A106"),
                Line(2, task: "L110", taskAsBilled: "ZZZ"),
                Line(3, taskAsBilled: "L110", activityAsBilled: "A106"),
                Line(4),
                Line(5, timekeeper: TimekeeperA, timekeeperName: "Jane Partner"),
                Line(6, timekeeper: TimekeeperA, timekeeperName: "Jane Partner"),
            ],
        };

        await h.Build().ProcessAsync(Job(), CancellationToken.None);

        var doc = h.OnlyDocument();
        ((string[])doc["taskCodes"]).Should().Equal("L110", "L120");
        ((string[])doc["activityCodes"]).Should().Equal("A106");
        ((string[])doc["expenseCodes"]).Should().BeEmpty();
        ((string[])doc["timekeeperIds"]).Should().Equal(TimekeeperA.ToString("D"));
        ((string[])doc["timekeeperNames"]).Should().Equal("Jane Partner");
    }

    [Fact(DisplayName = "152 FR-43: only the invoice and line tables are read - never a document, a Stored File or a submission")]
    public async Task OnlyTheInvoiceAndLineTablesAreRead()
    {
        var h = new Harness();

        await h.Build().ProcessAsync(Job(), CancellationToken.None);

        h.QueriedTables.Should().BeEquivalentTo("sprk_invoice", "sprk_billingevent");
    }

    [Fact(DisplayName = "152: the line query asks for the invoice's lines only and leaves out a removed (inactive) Adjustment line")]
    public void LineQuery_IsTheInvoicesOwnActiveLines()
    {
        var query = InvoiceIndexDocument.LineQuery(InvoiceId);

        query.EntityName.Should().Be("sprk_billingevent");
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_invoice" && c.Values.Contains(InvoiceId));
        query.Criteria.Filters.Should().ContainSingle(f => f.Conditions.Any(c => c.AttributeName == "statecode"),
            "ExcludeInactiveAdjustments, as every other reader of an invoice's lines");
    }

    // ── Tenant, idempotence, status changes ──

    [Fact(DisplayName = "152 ADR-014: tenantId is the configured AzureAd:TenantId (the value the search filters on), never a default")]
    public async Task TenantId_IsTheSearchFiltersValue()
    {
        var h = new Harness();
        h.Settings["AzureAd:TenantId"] = "11111111-2222-3333-4444-555555555555";

        await h.Build().ProcessAsync(Job(), CancellationToken.None);

        h.OnlyDocument()["tenantId"].Should().Be("11111111-2222-3333-4444-555555555555");
    }

    [Theory(DisplayName = "152 ADR-014: with no tenant configured the job is poisoned and nothing is written")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingTenant_IsPoisoned_NothingWritten(string? tenant)
    {
        var h = new Harness();
        h.Settings["AzureAd:TenantId"] = tenant;

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        h.Batches.Should().BeEmpty();
    }

    [Fact(DisplayName = "152 NFR-08: re-delivery of one message rewrites the same document")]
    public async Task Redelivery_ProducesTheSameDocument()
    {
        var job = Job();
        var first = new Harness();
        var second = new Harness();

        await first.Build().ProcessAsync(job, CancellationToken.None);
        await second.Build().ProcessAsync(job, CancellationToken.None);

        var a = first.OnlyDocument();
        var b = second.OnlyDocument();
        a.Keys.Should().BeEquivalentTo(b.Keys);
        foreach (var key in a.Keys.Where(k => k != "contentVector"))
        {
            if (a[key] is string[] left)
            {
                left.Should().Equal((string[])b[key], key);
            }
            else
            {
                a[key].Should().Be(b[key], key);
            }
        }
    }

    [Fact(DisplayName = "152: a status or approval change rewrites the SAME document key with the new status, approved total and text")]
    public async Task StatusChange_UpdatesTheSameDocument()
    {
        var before = new Harness();
        await before.Build().ProcessAsync(Job("v5"), CancellationToken.None);

        var after = new Harness { InvoiceRow = Invoice(statusCode: InvoiceStatusCodes.Approved) };
        after.InvoiceRow!["sprk_approvedtotal"] = new Money(900m);
        await after.Build().ProcessAsync(Job("v6"), CancellationToken.None);

        var oldDoc = before.OnlyDocument();
        var newDoc = after.OnlyDocument();
        newDoc["id"].Should().Be(oldDoc["id"]);
        newDoc["statusCode"].Should().Be(InvoiceStatusCodes.Approved);
        newDoc["status"].Should().Be("Approved");
        newDoc["approvedTotal"].Should().Be(900d);
        ((string)newDoc["content"]).Should().Contain("Status: Approved").And.NotContain("Status: InReview");
    }

    // ── Enqueue keys ──

    [Fact(DisplayName = "152 ADR-004: the enqueue key carries the change - two changes never share a key, one change always does")]
    public void BuildJob_KeysDifferPerChange()
    {
        var acceptance = InvoiceIndexingJobHandler.BuildJob(InvoiceId, InvoiceIndexingJobHandler.AcceptedChange, null);
        var approved = InvoiceIndexingJobHandler.BuildJob(InvoiceId, "v7", null);
        var sentToAp = InvoiceIndexingJobHandler.BuildJob(InvoiceId, "v9", null);
        var approvedAgain = InvoiceIndexingJobHandler.BuildJob(InvoiceId, "v7", null);

        new[] { acceptance.IdempotencyKey, approved.IdempotencyKey, sentToAp.IdempotencyKey }.Should().OnlyHaveUniqueItems();
        approvedAgain.IdempotencyKey.Should().Be(approved.IdempotencyKey);
        approved.IdempotencyKey.Should().Be($"invoice-index-{InvoiceId:N}-v7");
        approved.JobType.Should().Be("InvoiceIndexing");
        approved.SubjectId.Should().Be(InvoiceId.ToString());
        approved.JobId.Should().NotBe(approvedAgain.JobId, "a job is a new message even when its key repeats");
        approved.Payload!.RootElement.GetProperty("InvoiceId").GetGuid().Should().Be(InvoiceId);
    }

    [Fact(DisplayName = "152: BuildJob refuses an empty invoice id and an empty change")]
    public void BuildJob_RefusesEmptyInputs()
    {
        FluentActions.Invoking(() => InvoiceIndexingJobHandler.BuildJob(Guid.Empty, "v1", null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => InvoiceIndexingJobHandler.BuildJob(InvoiceId, " ", null)).Should().Throw<ArgumentException>();
    }

    // ── Payloads ──

    [Fact(DisplayName = "152: a legacy payload with a documentId (the retired extraction path) indexes the invoice and ignores the document")]
    public async Task LegacyPayload_IsAccepted_DocumentIsIgnored()
    {
        var h = new Harness { InvoiceRow = Invoice(withDocument: false) };

        var outcome = await h.Build().ProcessAsync(
            JobWithPayload($$"""{"InvoiceId":"{{InvoiceId}}","DocumentId":"{{Guid.NewGuid()}}","EnqueuedAt":"2026-10-30T00:00:00Z"}"""),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.OnlyDocument()["documentId"].Should().BeNull("the document id comes from the invoice's own lookup, not the message");
        h.QueriedTables.Should().BeEquivalentTo("sprk_invoice", "sprk_billingevent");
    }

    [Theory(DisplayName = "152: an empty or unreadable payload, and an invoice that is not there, are poisoned")]
    [InlineData("{\"InvoiceId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task BadPayload_IsPoisoned(string json)
    {
        var h = new Harness();

        var outcome = await h.Build().ProcessAsync(JobWithPayload(json), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        h.Batches.Should().BeEmpty();
    }

    [Fact(DisplayName = "152: an invoice that is not in Dataverse is poisoned and nothing is written")]
    public async Task MissingInvoice_IsPoisoned()
    {
        var h = new Harness { InvoiceRow = null };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        h.Batches.Should().BeEmpty();
    }

    // ── The embedding ──

    [Fact(DisplayName = "152: with no AI facade the document is indexed WITHOUT a contentVector key, so a Merge keeps the vector an earlier run wrote")]
    public async Task NoAiFacade_IndexesWithoutAVectorKey()
    {
        var h = new Harness { UseAi = false };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var doc = h.OnlyDocument();
        doc.ContainsKey("contentVector").Should().BeFalse();
        doc["vendorName"].Should().Be("Acme Counsel LLP", "keyword and filter search still work");
    }

    [Fact(DisplayName = "152: the Null AI facade (kill switch off) takes the same path as no facade: indexed, no vector key")]
    public async Task NullAiFacade_IndexesWithoutAVectorKey()
    {
        var h = new Harness { Ai = new NullInvoiceAi(NullLogger<NullInvoiceAi>.Instance) };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.OnlyDocument().ContainsKey("contentVector").Should().BeFalse();
    }

    [Fact(DisplayName = "152: an embedding that fails (throttled, model error) still indexes the document, without a vector key")]
    public async Task EmbeddingFault_IndexesWithoutAVectorKey()
    {
        var ai = new Mock<IInvoiceAi>();
        ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429"));
        var h = new Harness { Ai = ai.Object };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.OnlyDocument().ContainsKey("contentVector").Should().BeFalse();
    }

    [Fact(DisplayName = "152: a very long invoice is embedded from its head only, under the model's input limit, and the stored content is whole")]
    public async Task LongInvoice_EmbeddingInputIsCapped_ContentIsNot()
    {
        var h = new Harness
        {
            Lines = Enumerable.Range(1, 400).Select(i => Line(i, description: new string('x', 200) + i)).ToList(),
        };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.EmbeddingInputs.Should().ContainSingle().Which.Length.Should().Be(InvoiceIndexDocument.EmbeddingInputMaxChars);
        ((string)h.OnlyDocument()["content"]).Length.Should().BeGreaterThan(InvoiceIndexDocument.EmbeddingInputMaxChars);
    }

    // ── Search outcomes ──

    [Fact(DisplayName = "152: a per-document failure from the search is retried (Failure), not swallowed")]
    public async Task FailedIndexResult_IsRetried()
    {
        var h = new Harness { Result = SearchModelFactory.IndexingResult("k", "throttled", false, 503) };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
    }

    [Theory(DisplayName = "152: throttling and unavailability retry; other search faults are permanent")]
    [InlineData(429, JobStatus.Failed)]
    [InlineData(503, JobStatus.Failed)]
    [InlineData(400, JobStatus.Poisoned)]
    public async Task SearchRequestFault_RetriesOnlyWhenTransient(int status, JobStatus expected)
    {
        var h = new Harness { SearchFault = new RequestFailedException(status, "search said no") };

        var outcome = await h.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(expected);
    }

    // ── The document matches the index schema ──

    [Fact(DisplayName = "152: every field the handler writes exists in spaarke-invoices-index.json with the right shape; tenantId is filterable; the vector stays 3072-dim")]
    public async Task EveryWrittenField_IsInTheIndexSchema()
    {
        var h = new Harness();
        await h.Build().ProcessAsync(Job(), CancellationToken.None);
        var doc = h.OnlyDocument();

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "ai-search", "spaarke-invoices-index.json")));
        var fields = schema.RootElement.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => f);

        foreach (var key in doc.Keys)
        {
            fields.Should().ContainKey(key, $"the handler writes '{key}'");
            var type = fields[key].GetProperty("type").GetString();
            if (doc[key] is string[])
            {
                type.Should().Be("Collection(Edm.String)", key);
            }
        }

        fields["tenantId"].GetProperty("filterable").GetBoolean().Should().BeTrue();
        fields["contentVector"].GetProperty("dimensions").GetInt32().Should().Be(3072);
        foreach (var filter in new[] { "vendorOrgId", "coreRecordType", "coreRecordId", "timekeeperIds", "taskCodes", "activityCodes", "expenseCodes", "statusCode" })
        {
            fields[filter].GetProperty("filterable").GetBoolean().Should().BeTrue(filter);
        }

        foreach (var facet in new[] { "taskCodes", "activityCodes", "expenseCodes", "timekeeperIds" })
        {
            fields[facet].GetProperty("facetable").GetBoolean().Should().BeTrue(facet);
        }
    }

    [Fact(DisplayName = "152: the deployer's invoices-index invariants require every firm / core-record / code / status filter the document is found by")]
    public void DeployerInvariants_NameTheNewFilters()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ai-search", "Deploy-AllIndexes.ps1"));
        var entry = script[script.IndexOf("Key        = 'invoices-index'", StringComparison.Ordinal)..];
        var required = entry[entry.IndexOf("RequiredFilterableFields", StringComparison.Ordinal)..].Split('\n')[0];

        foreach (var field in new[] { "tenantId", "invoiceId", "matterId", "projectId", "vendorOrgId", "coreRecordType", "coreRecordId", "timekeeperIds", "taskCodes", "activityCodes", "expenseCodes", "statusCode" })
        {
            required.Should().Contain($"'{field}'");
        }
    }

    // ── InvoiceSearchService still binds ──

    [Fact(DisplayName = "152: the search result binds a document with no documentId (Guid.Empty) and one with a documentId")]
    public void SearchResult_BindsWithAndWithoutADocumentId()
    {
        var withoutDocument = InvoiceSearchService.CreateSearchResultItem(
            new InvoiceSearchDocument { InvoiceId = InvoiceId.ToString(), DocumentId = null, VendorName = "Acme", TotalAmount = 12.5, Currency = "USD" }, 0.5, null);
        var withDocument = InvoiceSearchService.CreateSearchResultItem(
            new InvoiceSearchDocument { InvoiceId = InvoiceId.ToString(), DocumentId = DocumentId.ToString() }, 0.5, null);
        var garbled = InvoiceSearchService.CreateSearchResultItem(
            new InvoiceSearchDocument { InvoiceId = InvoiceId.ToString(), DocumentId = "not-a-guid" }, 0.5, null);

        withoutDocument.InvoiceId.Should().Be(InvoiceId);
        withoutDocument.DocumentId.Should().Be(Guid.Empty);
        withoutDocument.VendorName.Should().Be("Acme");
        withDocument.DocumentId.Should().Be(DocumentId);
        garbled.DocumentId.Should().Be(Guid.Empty);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
