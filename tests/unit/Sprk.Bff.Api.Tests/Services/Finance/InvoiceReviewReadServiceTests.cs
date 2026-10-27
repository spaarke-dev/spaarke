using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 100 (FR-31, NFR-02): <see cref="InvoiceReviewReadService"/>. The Dataverse seam is
/// <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>, answered from fixture rows by the query's entity,
/// with every query kept: the bounded query count is asserted, not assumed. The stored findings are the JSON task 049 writes (a round trip with
/// the persister's own writer is in <c>InvoiceSubmissionPersisterTests</c>).
/// </summary>
public sealed class InvoiceReviewReadServiceTests
{
    private static readonly Guid InvoiceId = Guid.Parse("10000000-0000-4000-8000-0000000000a1");
    private static readonly Guid SubmissionId = Guid.Parse("10000000-0000-4000-8000-0000000000b1");
    private static readonly Guid FirmId = Guid.Parse("10000000-0000-4000-8000-0000000000f1");
    private static readonly Guid MatterId = Guid.Parse("10000000-0000-4000-8000-0000000000c1");
    private static readonly Guid PolicyId = Guid.Parse("10000000-0000-4000-8000-0000000000d1");
    private static readonly Guid VersionId = Guid.Parse("10000000-0000-4000-8000-0000000000d2");
    private static readonly Guid TimekeeperId = Guid.Parse("10000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Reviewer = Guid.Parse("10000000-0000-4000-8000-0000000000e2");
    private static readonly InvoiceContextService.InvoiceCaller Caller = new("caller-oid", "caller-token");

    private sealed class Rights : IAccessDataSource
    {
        public AccessRights Granted = AccessRights.Read | AccessRights.Write;
        public bool Faulted;

        public Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
            Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = recordId.ToString(), AccessRights = Faulted ? AccessRights.None : Granted, Faulted = Faulted });

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("The review asks about the invoice only.");
    }

    private sealed class World
    {
        public Entity? Invoice;
        public List<Entity> Lines = [];
        public Entity? Submission;
        public List<Entity> History = [];
        public List<Entity> Roster = [];

        /// <summary>Task 092: the Servable Invoice PDF Stored Files the query returns (Dataverse applies the filter; the query shape is asserted).</summary>
        public List<Entity> Files = [];
        public readonly List<QueryExpression> Queries = [];
        public readonly Rights Access = new();
        public decimal Threshold = 0.90m;
        public bool SettingsFault;
        public readonly Mock<IGenericEntityService> Entities = new(MockBehavior.Strict);

        // D-176 (task 119): the CALLER's Dataverse, an evaluating double like ontology 157's. The invoice read returns the In-listed ids the
        // caller reads; any other read fails the test (#2294: no tracking number is looked up). Every path the caller is asked is kept.
        public readonly HashSet<Guid> ReadableInvoices = [];
        public readonly List<string> CallerPaths = [];
        public Func<string, Task<DataverseUserResponse>>? CallerFault;
        public readonly Mock<ILogger<InvoiceReviewReadService>> Logger = new();

        public World(int status = InvoiceStatusCodes.InReview)
        {
            Invoice = new Entity("sprk_invoice", InvoiceId)
            {
                ["sprk_name"] = "INV-100",
                ["sprk_invoicenumber"] = "F-100",
                ["sprk_trackingnumber"] = "TRK-1",
                ["sprk_invoicedate"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
                ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId) { Name = "Firm LLP" },
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) { Name = "Acme v Roe" },
                ["sprk_totalamount"] = new Money(1000m),
                ["sprk_feestotal"] = new Money(800m),
                ["sprk_expensestotal"] = new Money(200m),
                ["sprk_lineadjustmentstotal"] = new Money(50m),
                ["sprk_invoiceadjustmentstotal"] = new Money(0m),
                ["sprk_approvedtotal"] = new Money(950m),
                ["statuscode"] = new OptionSetValue(status),
            };

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    lock (Queries)
                    {
                        Queries.Add(query);
                    }

                    return query.EntityName switch
                    {
                        "sprk_invoice" => new EntityCollection(Invoice is null ? [] : [Invoice]),
                        "sprk_billingevent" => new EntityCollection(Lines.ToList()),
                        "sprk_invoicesubmission" => new EntityCollection(Submission is null ? [] : [Requested(Submission, query.ColumnSet)]),
                        "sprk_decisionrecord" => new EntityCollection(History.ToList()),
                        "sprk_timekeeper" => new EntityCollection(Roster.ToList()),
                        "sprk_storedfile" => new EntityCollection(Files.ToList()),
                        _ => throw new InvalidOperationException($"Unexpected read of {query.EntityName}"),
                    };
                });
        }

        /// <summary>The row as Dataverse returns it: only the requested columns, so a column the service forgot to ask for is absent (119 round 4).</summary>
        private static Entity Requested(Entity row, ColumnSet columns)
        {
            var projected = new Entity(row.LogicalName, row.Id);
            foreach (var column in columns.Columns.Where(row.Contains))
            {
                projected[column] = row[column];
            }

            return projected;
        }

        public int Reads(string entity) => Queries.Count(q => q.EntityName == entity);

        public InvoiceReviewReadService Service()
        {
            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                .Returns(() => SettingsFault
                    ? Task.FromException<EbillingSettings>(new InvalidOperationException("settings down"))
                    : Task.FromResult(new EbillingSettings { ReliabilityThreshold = Threshold }));
            var authorization = new AuthorizationService(
                Access, [new OperationAccessRule(NullLogger<OperationAccessRule>.Instance)], NullLogger<AuthorizationService>.Instance);
            var caller = new Mock<IDataverseUserClient>(MockBehavior.Strict);
            caller.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken _) => AnswerCaller(path));
            return new InvoiceReviewReadService(
                Entities.Object, authorization, settings.Object, caller.Object,
                new EarlierInvoiceReadability(NullLogger<EarlierInvoiceReadability>.Instance), Logger.Object);
        }

        public int CallerReads(string set) => CallerPaths.Count(p => p.StartsWith(set + "?", StringComparison.Ordinal));

        private Task<DataverseUserResponse> AnswerCaller(string path)
        {
            CallerPaths.Add(path);
            if (CallerFault is not null)
            {
                return CallerFault(path);
            }

            var set = path[..path.IndexOf('?')];
            var filter = Uri.UnescapeDataString(path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..]);
            object rows = set switch
            {
                "sprk_invoices" => Regex.Matches(filter, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value))
                    .Where(ReadableInvoices.Contains)
                    .Select(id => new Dictionary<string, object?> { ["sprk_invoiceid"] = id })
                    .ToList(),
                _ => throw new InvalidOperationException($"unexpected caller read {path}"),
            };
            return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows })));
        }

        public Entity AddLine(int sequence, InvoiceLineStatus status, decimal amount = 100m, string? extraction = null, Guid? timekeeper = null)
        {
            var line = new Entity("sprk_billingevent", LineId(sequence))
            {
                ["sprk_name"] = $"Line {sequence}",
                ["sprk_linesequence"] = sequence,
                ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
                ["sprk_eventdate"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ["sprk_timekeeper"] = "J. Smith",
                ["sprk_quantity"] = 2.0m,
                ["sprk_rate"] = new Money(50m),
                ["sprk_approvedrate"] = new Money(45m),
                ["sprk_amount"] = new Money(amount),
                ["sprk_description"] = "Draft the response",
                ["sprk_taskcodeasbilled"] = "L110",
                ["sprk_codingconfidence"] = 0.81m,
                ["sprk_linestatus"] = new OptionSetValue((int)status),
                ["versionnumber"] = 5L,
            };
            if (extraction is not null) line["sprk_extractionconfidence"] = extraction;
            if (timekeeper is not null) line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", timekeeper.Value);
            Lines.Add(line);
            return line;
        }
    }

    /// <summary>The id of the line with 1-based <paramref name="sequence"/>: the persister's rule, which the plan subjects of the stored findings follow too (D-176 K4).</summary>
    private static Guid LineId(int sequence) => InvoiceSubmissionPersister.LineIdFor(SubmissionId, sequence - 1);

    private const string Extraction =
        "{\"fields\":{\"Hours\":{\"confidence\":0.95,\"combined\":0.70,\"page\":3,\"repair\":\"None\",\"box\":{\"page\":3,\"x\":0.1,\"y\":0.2,\"w\":0.3,\"h\":0.05}},"
        + "\"Rate\":{\"confidence\":0.99,\"combined\":0.99,\"page\":3,\"repair\":\"None\"},"
        + "\"Narrative\":{\"confidence\":null,\"combined\":null,\"page\":null,\"repair\":\"None\"}},"
        + "\"coding\":{\"status\":\"Coded\",\"needsReview\":true,\"confidence\":0.81,\"task\":{\"code\":\"L120\",\"firmCode\":\"L110\",\"mismatch\":true},"
        + "\"activity\":{\"code\":\"A101\",\"firmCode\":null,\"mismatch\":false},\"expense\":{\"code\":null,\"firmCode\":null,\"mismatch\":false},"
        + "\"refused\":[{\"kind\":\"Task\",\"code\":\"ZZZ\",\"reason\":\"NotInSet\"}]}}";

    /// <summary>The findings JSON 049 stores: a line finding with a suggested adjustment (plan primary reason + a second reason) and an invoice finding.</summary>
    private static string FindingsJson(bool withheld = false)
    {
        var line = LineId(1);
        var finding = new JsonObject
        {
            ["index"] = 0, ["ruleType"] = "RateCard", ["policyId"] = PolicyId.ToString("D"), ["policyCode"] = "BILL-RATE",
            ["policyVersionId"] = VersionId.ToString("D"), ["clause"] = "Schedule A, section 2", ["clauseLocation"] = "page 4",
            ["subject"] = "Line", ["line"] = 0, ["reasonCode"] = "rate.above_card", ["detailWithheld"] = withheld,
            ["consequence"] = "Reject", ["rejects"] = true, ["heldBecause"] = null,
        };
        if (!withheld)
        {
            finding["valuesRead"] = new JsonArray(new JsonObject { ["name"] = "BilledRate", ["value"] = "50", ["line"] = 0, ["field"] = "Rate", ["confidence"] = 0.99, ["belowReliabilityThreshold"] = false });
            finding["suggestedAdjustment"] = new JsonObject { ["kind"] = "Reduce", ["amount"] = 10.0 };
        }

        var second = new JsonObject
        {
            ["index"] = 1, ["ruleType"] = "RateCard", ["policyId"] = PolicyId.ToString("D"), ["policyCode"] = "BILL-RATE",
            ["policyVersionId"] = VersionId.ToString("D"), ["clause"] = "Schedule A, section 3", ["clauseLocation"] = null,
            ["subject"] = "Line", ["line"] = 0, ["reasonCode"] = "rate.increase_unapproved", ["detailWithheld"] = withheld,
            ["consequence"] = "Flag", ["rejects"] = false, ["heldBecause"] = null,
        };
        var invoiceFinding = new JsonObject
        {
            ["index"] = 2, ["ruleType"] = "Staffing", ["policyId"] = Guid.NewGuid().ToString("D"), ["policyCode"] = "BILL-STAFF",
            ["policyVersionId"] = Guid.NewGuid().ToString("D"), ["clause"] = "Guideline 7", ["clauseLocation"] = null,
            ["subject"] = "Invoice", ["line"] = null, ["reasonCode"] = "staffing.too_many", ["detailWithheld"] = false,
            ["consequence"] = "Flag", ["rejects"] = false, ["heldBecause"] = null,
        };
        var root = new JsonObject
        {
            ["schema"] = 1, ["kind"] = "Accepted", ["reasonCode"] = null,
            ["decision"] = new JsonObject { ["outcome"] = "Rejected" },
            ["findings"] = new JsonArray(finding, second, invoiceFinding),
            ["pipelineFindings"] = new JsonArray(new JsonObject { ["reasonCode"] = "invoice_number_mismatch", ["values"] = new JsonObject { ["read"] = "x" } }),
            ["signalPlan"] = new JsonArray(
                new JsonObject
                {
                    ["policyCode"] = "BILL-RATE", ["subjectId"] = line.ToString("D"), ["severity"] = 100000002,
                    ["sentence"] = "The rate is above the agreed card.", ["reasonCodes"] = new JsonArray("rate.above_card", "rate.increase_unapproved"),
                },
                new JsonObject
                {
                    ["policyCode"] = "BILL-STAFF", ["subjectId"] = InvoiceId.ToString("D"), ["severity"] = 100000001,
                    ["sentence"] = "Too many timekeepers.", ["reasonCodes"] = new JsonArray("staffing.too_many"),
                }),
        };
        return root.ToJsonString();
    }

    private static Entity SubmissionRow(string findings, bool blocked = false) => new("sprk_invoicesubmission", SubmissionId)
    {
        ["sprk_processingstatus"] = new OptionSetValue((int)(blocked ? InvoiceSubmissionStatus.Rejected : InvoiceSubmissionStatus.Accepted)),
        ["sprk_outcomereasoncode"] = blocked ? SubmissionReasonCodes.Blocked : null,
        ["sprk_findingsjson"] = findings,
        ["sprk_trackingnumber"] = "TRK-1",
        // The firm the submission's priors were read from; the same org as the invoice's unless a test changes the invoice's (119 round 4).
        ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
        ["createdon"] = DateTime.UtcNow,
    };

    private static Entity Record(string action, Guid? line, DateTime at, int outcome = 100000000) => new("sprk_decisionrecord", Guid.NewGuid())
    {
        ["sprk_decisionnumber"] = "DR-1",
        ["sprk_actioncode"] = action,
        ["sprk_recordclass"] = new OptionSetValue(100000000),
        ["sprk_decisionoutcome"] = new OptionSetValue(outcome),
        ["sprk_reason"] = "because",
        ["sprk_decidedon"] = at,
        ["sprk_confirmedby"] = new EntityReference("systemuser", Reviewer) { Name = "R. Reviewer" },
        ["sprk_regardinginvoice"] = line is null ? new EntityReference("sprk_invoice", InvoiceId) : null!,
        ["sprk_regardinginvoiceline"] = line is null ? null! : new EntityReference("sprk_billingevent", line.Value),
    };

    // ── The model ──

    [Fact(DisplayName = "100: the read model carries the header, the stored totals, the lines in order and the line counts by status")]
    public async Task ReturnsHeaderTotalsAndLines()
    {
        var w = new World();
        w.AddLine(2, InvoiceLineStatus.Flagged);
        w.AddLine(1, InvoiceLineStatus.Pending);
        w.AddLine(3, InvoiceLineStatus.Reduced).Attributes["sprk_approvedamount"] = new Money(60m);

        var review = await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review.Should().NotBeNull();
        review!.Header.FirmInvoiceNumber.Should().Be("F-100");
        review.Header.TrackingNumber.Should().Be("TRK-1");
        review.Header.Firm!.Name.Should().Be("Firm LLP");
        review.Header.CoreRecord.Should().BeEquivalentTo(new ReviewCoreRecordDto("matter", MatterId, "Acme v Roe"));
        review.Header.Status.Should().Be("InReview");
        review.Totals.Should().Be(new ReviewTotalsDto(1000m, 800m, 200m, 50m, 0m, 950m));
        review.Lines.Select(l => l.Sequence).Should().Equal(1, 2, 3);
        review.LineCounts["Pending"].Should().Be(1);
        review.LineCounts["Flagged"].Should().Be(1);
        review.LineCounts["Reduced"].Should().Be(1);
        review.LineCounts["Clean"].Should().Be(0, "every status is listed, zeros included");
        review.Lines[2].ApprovedAmount.Should().Be(60m);
    }

    // -- ebilling-r1 task 092 (model 9.2): the invoice PDF the review page loads is the invoice's Servable Stored File --

    [Fact(DisplayName = "092: the review carries the id of the invoice's Servable Invoice PDF Stored File, asked by invoice, purpose and status, newest first")]
    public async Task InvoicePdfFileId_IsTheServableInvoicePdf()
    {
        var fileId = Guid.Parse("10000000-0000-4000-8000-0000000000aa");
        var w = new World { Files = [new Entity(StoredFileSchema.Entity, fileId)] };

        var review = await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review!.InvoicePdfFileId.Should().Be(fileId);
        var query = w.Queries.Single(q => q.EntityName == StoredFileSchema.Entity);
        query.TopCount.Should().Be(1);
        query.ColumnSet.Columns.Should().Equal(StoredFileSchema.IdColumn);
        query.Criteria.Conditions.Select(c => (c.AttributeName, c.Operator, c.Values.Single())).Should().BeEquivalentTo(new (string, ConditionOperator, object)[]
        {
            (StoredFileSchema.InvoiceTable, ConditionOperator.Equal, InvoiceId),
            (StoredFileSchema.Purpose, ConditionOperator.Equal, (int)StoredFilePurpose.InvoicePdf),
            (StoredFileSchema.Status, ConditionOperator.Equal, (int)StoredFileStatus.Servable),
        });
        query.Orders.Should().ContainSingle(o => o.AttributeName == StoredFileSchema.CreatedOn && o.OrderType == OrderType.Descending);
    }

    [Fact(DisplayName = "092 must-not-fire: no Servable Invoice PDF (an existing-document confirm, or a file not yet servable) gives a null id")]
    public async Task InvoicePdfFileId_IsNull_WithoutAServableFile()
    {
        var review = await new World().Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review!.InvoicePdfFileId.Should().BeNull();
    }

    [Fact(DisplayName = "100: a Pending line is shown as Processing (D-58) and the stored status name is kept")]
    public async Task PendingIsShownAsProcessing()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Pending);

        var line = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!.Lines.Single();

        line.Status.Should().Be("Pending");
        line.StatusLabel.Should().Be("Processing");
    }

    [Fact(DisplayName = "100: per-field page, box and confidence come back; a field below the reliability threshold is marked low-confidence; codes show as billed and resolved")]
    public async Task ExtractionFieldsAndCodes()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Clean, extraction: Extraction, timekeeper: TimekeeperId);
        w.Roster = [new Entity("sprk_timekeeper", TimekeeperId) { ["sprk_name"] = "Jane Smith", ["sprk_classification"] = new OptionSetValue(100000003) }];

        var line = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!.Lines.Single();

        var hours = line.Fields["Hours"];
        hours.Page.Should().Be(3);
        hours.Box.Should().BeEquivalentTo(new ReviewBoxDto(3, 0.1m, 0.2m, 0.3m, 0.05m));
        hours.Confidence.Should().Be(0.95m);
        hours.Combined.Should().Be(0.70m);
        hours.LowConfidence.Should().BeTrue("the combined confidence 0.70 is below the 0.90 threshold");
        line.Fields["Rate"].LowConfidence.Should().BeFalse();
        line.Fields["Narrative"].LowConfidence.Should().BeFalse("no confidence known: the marker is for a known low value");
        line.Codes.Task.AsBilled.Should().Be("L110");
        line.Codes.Task.Code.Should().Be("L120");
        line.Codes.Task.Mismatch.Should().BeTrue();
        line.Codes.Confidence.Should().Be(0.81m);
        line.Codes.CodeSetNeedsReview.Should().BeTrue();
        line.Codes.Refused.Should().Equal("ZZZ");
        line.Timekeeper.Should().BeEquivalentTo(new ReviewTimekeeperDto(TimekeeperId, "Jane Smith", 100000003));
        line.TimekeeperAsBilled.Should().Be("J. Smith");
        line.Quantity.Should().Be(2.0m);
        line.ApprovedRate.Should().Be(45m);
    }

    [Fact(DisplayName = "100: line findings carry policy, version, clause, location, values read, severity and the suggested adjustment; only the primary reason carries the sentence")]
    public async Task LineFindings()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(FindingsJson());

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        var findings = review.Lines.Single().Findings;
        findings.Should().HaveCount(2);
        var primary = findings[0];
        primary.PolicyCode.Should().Be("BILL-RATE");
        primary.PolicyId.Should().Be(PolicyId);
        primary.PolicyVersionId.Should().Be(VersionId);
        primary.Clause.Should().Be("Schedule A, section 2");
        primary.ClauseLocation.Should().Be("page 4");
        primary.ReasonCode.Should().Be("rate.above_card");
        primary.Message.Should().Be("The rate is above the agreed card.");
        primary.Severity.Should().Be("Critical");
        primary.Consequence.Should().Be("Reject");
        primary.Rejects.Should().BeTrue();
        primary.SuggestedAdjustment.Should().BeEquivalentTo(new ReviewSuggestionDto("Reduce", 10m));
        primary.ValuesRead.Should().ContainSingle().Which.Should().BeEquivalentTo(new ReviewValueReadDto("BilledRate", "50", "Rate", 0.99m, false));
        findings[1].ReasonCode.Should().Be("rate.increase_unapproved");
        findings[1].Message.Should().BeNull("the Signal read route withholds a non-primary reason's text (D-123): the screen words it from the code");
        review.Lines.Single().FindingCount.Should().Be(2);
        review.Lines.Single().HighestSeverity.Should().Be("Critical");
    }

    [Fact(DisplayName = "100: invoice-level findings, pipeline findings and the decision come from the same stored copy")]
    public async Task InvoiceFindings_AndSubmission()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(FindingsJson());

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        review.InvoiceFindings.Should().ContainSingle().Which.ReasonCode.Should().Be("staffing.too_many");
        review.InvoiceFindings[0].Message.Should().Be("Too many timekeepers.");
        review.InvoiceFindings[0].Severity.Should().Be("Warning");
        review.Submission!.Decision.Should().Be("Rejected");
        review.Submission.PipelineFindings.Should().ContainSingle().Which.ReasonCode.Should().Be("invoice_number_mismatch");
    }

    [Fact(DisplayName = "100: a withheld group returns no values read and no suggestion (D-112)")]
    public async Task WithheldGroup_ReturnsNoDetail()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(FindingsJson(withheld: true));

        var findings = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!.Lines.Single().Findings;

        findings.Should().OnlyContain(f => f.DetailWithheld && f.ValuesRead.Count == 0 && f.SuggestedAdjustment == null);
    }

    [Fact(DisplayName = "100: a blocked internal submission is held; an override Decision Record marks the blocking finding overridden")]
    public async Task HeldAndOverridden()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(FindingsJson(), blocked: true);

        var held = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;
        held.Header.Held.Should().BeTrue();
        held.Lines.Single().Findings.Should().OnlyContain(f => !f.Overridden);

        w.Submission = SubmissionRow(FindingsJson());
        w.History = [Record("override-submission", null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))];
        var overridden = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;
        overridden.Header.Held.Should().BeFalse();
        overridden.Lines.Single().Findings.Single(f => f.Rejects).Overridden.Should().BeTrue();
        overridden.Lines.Single().Findings.Single(f => !f.Rejects).Overridden.Should().BeFalse("only a finding that rejected was overridden");
    }

    [Fact(DisplayName = "100: the Decision Record history lists invoice and line decisions in order")]
    public async Task DecisionHistory()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Reduced);
        w.History =
        [
            Record("reduce-line", LineId(1), new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)),
            Record("override-submission", null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
        ];

        var history = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!.DecisionHistory;

        history.Should().HaveCount(2);
        history.Single(h => h.ActionCode == "reduce-line").LineId.Should().Be(LineId(1));
        history.Single(h => h.ActionCode == "reduce-line").Subject.Should().Be("line");
        history.Single(h => h.ActionCode == "override-submission").Subject.Should().Be("invoice");
        history[0].RecordClass.Should().Be("Judgement");
        history[0].Outcome.Should().Be("Authorized");
        history[0].DecidedBy!.Name.Should().Be("R. Reviewer");
    }

    // ── NFR-02: the number of Dataverse reads does not depend on the number of lines ──

    [Theory(DisplayName = "100: the Dataverse read count is the same for 3 and 150 lines (none per line)")]
    [InlineData(3)]
    [InlineData(150)]
    public async Task ReadCount_DoesNotGrowWithTheLineCount(int lineCount)
    {
        var w = new World();
        for (var i = 1; i <= lineCount; i++)
        {
            w.AddLine(i, InvoiceLineStatus.Flagged, extraction: Extraction, timekeeper: TimekeeperId);
        }

        w.Submission = SubmissionRow(FindingsJson());
        w.Roster = [new Entity("sprk_timekeeper", TimekeeperId) { ["sprk_name"] = "Jane Smith" }];

        var review = await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review!.Lines.Should().HaveCount(lineCount);
        w.Queries.Should().HaveCount(6, "invoice, lines, submission, history, roster, invoice PDF (task 092) - whatever the line count");
        w.Reads("sprk_storedfile").Should().Be(1);
        w.Reads("sprk_billingevent").Should().Be(1);
        w.Reads("sprk_decisionrecord").Should().Be(1);
        w.Reads("sprk_timekeeper").Should().Be(1);
    }

    // ── canDecide, tolerance, absence ──

    [Theory(DisplayName = "100: canDecide needs Write on the invoice (asked as the caller) and an invoice In Review")]
    [InlineData(true, InvoiceStatusCodes.InReview, true)]
    [InlineData(false, InvoiceStatusCodes.InReview, false)]
    [InlineData(true, InvoiceStatusCodes.Approved, false)]
    public async Task CanDecide(bool write, int status, bool expected)
    {
        var w = new World(status);
        w.Access.Granted = write ? AccessRights.Read | AccessRights.Write : AccessRights.Read;
        w.AddLine(1, InvoiceLineStatus.Clean);

        var flags = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!.Flags;

        flags.CanRead.Should().BeTrue();
        flags.CanDecide.Should().Be(expected);
    }

    [Fact(DisplayName = "100: a rights lookup that faults, a damaged findings column and unreadable settings do not fail the page")]
    public async Task DegradesWithoutFailing()
    {
        var w = new World();
        w.Access.Faulted = true;
        w.SettingsFault = true;
        w.AddLine(1, InvoiceLineStatus.Flagged, extraction: Extraction);
        w.Submission = SubmissionRow("{ not json");

        var review = await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review.Should().NotBeNull();
        review!.Flags.CanDecide.Should().BeFalse("fail closed");
        review.Lines.Single().Findings.Should().BeEmpty();
        review.Lines.Single().Fields["Hours"].LowConfidence.Should().BeFalse("no threshold: the marker is off, the page still loads");
    }

    [Fact(DisplayName = "100: an invoice that does not exist reads as null")]
    public async Task UnknownInvoice_IsNull()
    {
        var w = new World { Invoice = null };

        (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None)).Should().BeNull();
    }

    // ── D-176 (task 119): a Duplicate's earlier invoice is judged as the caller (EarlierInvoiceReadability, ontology 157) ──

    private static readonly Guid EarlierInvoice = Guid.Parse("10000000-0000-4000-8000-0000000000a2");
    private static readonly Guid OtherEarlierInvoice = Guid.Parse("10000000-0000-4000-8000-0000000000a3");
    private const string EarlierNumber = "F-090.1";
    private const string DuplicateSentence = "Invoice F-100, the line dated 2026-09-01 for J. Smith, duplicates F-090.1.";

    private static JsonObject Value(string name, string? value, string? field = null) => new()
    {
        ["name"] = name, ["value"] = value, ["line"] = 0, ["field"] = field, ["confidence"] = field is null ? null : 0.97, ["belowReliabilityThreshold"] = false,
    };

    /// <summary>A stored finding as 049 writes DuplicateHandler's: the line's values, the current invoice number, then (when visible at write
    /// time) <c>duplicateOf.trackingNumber</c> and, on newer findings, <c>duplicateOf.invoiceId</c>; a write-time withheld one has no values.</summary>
    private static JsonObject Finding(
        int index, string policy, string reason, int? line, string? number = null, Guid? earlierInvoice = null, bool withheld = false, string ruleType = "Duplicate")
    {
        var finding = new JsonObject
        {
            ["index"] = index, ["ruleType"] = ruleType, ["policyId"] = PolicyId.ToString("D"), ["policyCode"] = policy,
            ["policyVersionId"] = VersionId.ToString("D"), ["clause"] = "Guideline 9", ["clauseLocation"] = null,
            ["subject"] = line is null ? "Invoice" : "Line", ["line"] = line, ["reasonCode"] = reason, ["detailWithheld"] = withheld,
            ["consequence"] = "Flag", ["rejects"] = false, ["heldBecause"] = null,
        };
        if (withheld)
        {
            return finding;
        }

        var values = new JsonArray(Value("line.timekeeperName", "J. Smith", "Timekeeper"), Value("line.date", "2026-09-01", "Date"), Value("invoice.firmInvoiceNumber", "F-100"));
        if (number is not null) values.Add(Value("duplicateOf.trackingNumber", number));
        if (earlierInvoice is { } id) values.Add(Value("duplicateOf.invoiceId", id.ToString("D")));
        finding["valuesRead"] = values;
        finding["suggestedAdjustment"] = new JsonObject { ["kind"] = "Disallow", ["amount"] = 100.0 };
        return finding;
    }

    private static JsonObject Plan(string policy, Guid subjectId, string sentence, params string[] reasons) => new()
    {
        ["policyCode"] = policy, ["subjectId"] = subjectId.ToString("D"), ["severity"] = 100000001, ["sentence"] = sentence,
        ["reasonCodes"] = new JsonArray(reasons.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
    };

    private static string Stored(JsonObject[] findings, JsonObject[] plan) => new JsonObject
    {
        ["schema"] = 1, ["kind"] = "Accepted", ["reasonCode"] = null, ["decision"] = new JsonObject { ["outcome"] = "Accepted" },
        ["findings"] = new JsonArray(findings.Cast<JsonNode>().ToArray()), ["pipelineFindings"] = new JsonArray(),
        ["signalPlan"] = new JsonArray(plan.Cast<JsonNode>().ToArray()),
    }.ToJsonString();

    /// <summary>A line with one Duplicate group (<c>BILL-DUP</c>) beside a RateCard group (<c>BILL-RATE</c>) on the same line, and an
    /// invoice-level Staffing finding: only the Duplicate group may change.</summary>
    private static World DuplicateWorld(string? number, Guid? earlierInvoice, string reason = "DuplicateOfPriorSubmissionLine")
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(Stored(
            [
                Finding(0, "BILL-DUP", reason, 0, number, earlierInvoice),
                Finding(1, "BILL-RATE", "rate.above_card", 0, ruleType: "RateCard"),
                Finding(2, "BILL-STAFF", "staffing.too_many", null, ruleType: "Staffing"),
            ],
            [
                Plan("BILL-DUP", LineId(1), DuplicateSentence, reason),
                Plan("BILL-RATE", LineId(1), "The rate is above the agreed card.", "rate.above_card"),
                Plan("BILL-STAFF", InvoiceId, "Too many timekeepers.", "staffing.too_many"),
            ]));
        return w;
    }

    private static void ShouldBeShown(ReviewFindingDto finding, string sentence)
    {
        finding.Message.Should().Be(sentence);
        finding.DetailWithheld.Should().BeFalse();
        finding.ValuesRead.Should().NotBeEmpty();
        finding.SuggestedAdjustment.Should().NotBeNull();
    }

    private static void ShouldBeWithheld(ReviewFindingDto finding, bool primary = true)
    {
        finding.Message.Should().Be(primary ? EarlierInvoiceReadability.WithheldSentence : null);
        finding.DetailWithheld.Should().BeTrue();
        finding.ValuesRead.Should().BeEmpty("no duplicateOf.* entry and no line values (the write-time withheld shape)");
        finding.SuggestedAdjustment.Should().BeNull();
    }

    /// <summary>The whole response, as serialized, names neither the earlier tracking number nor the earlier invoice id (119 round 2, K3).</summary>
    private static void ShouldNotLeak(InvoiceReviewDto review, string number, Guid invoice)
    {
        var json = JsonSerializer.Serialize(review);
        json.Should().NotContain(number);
        json.Should().NotContain(invoice.ToString("D"));
    }

    /// <summary>The non-Duplicate groups of <see cref="DuplicateWorld"/> come back exactly as stored.</summary>
    private static void OthersUntouched(InvoiceReviewDto review)
    {
        ShouldBeShown(review.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-RATE"), "The rate is above the agreed card.");
        ShouldBeShown(review.InvoiceFindings.Single(), "Too many timekeepers.");
    }

    [Fact(DisplayName = "119: a Duplicate whose earlier invoice (duplicateOf.invoiceId) the caller reads is returned unchanged, read as the caller")]
    public async Task ReadableEarlierInvoice_IsUnchanged()
    {
        var w = DuplicateWorld(EarlierNumber, EarlierInvoice);
        w.ReadableInvoices.Add(EarlierInvoice);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        var duplicate = review.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP");
        ShouldBeShown(duplicate, DuplicateSentence);
        duplicate.ValuesRead.Should().Contain(v => v.Name == "duplicateOf.trackingNumber" && v.Value == EarlierNumber);
        OthersUntouched(review);
        w.CallerReads("sprk_invoices").Should().Be(1, "the earlier invoice is read as the caller");
        w.CallerPaths.Should().ContainSingle("an id needs only the invoice read");
        w.Reads("sprk_invoice").Should().Be(1, "no app-only read of the earlier invoice: only the current invoice is read app-only");
    }

    // ── #2294 / DEF-043: a Duplicate identified only by a tracking number shows the generic withheld sentence ──

    [Theory(DisplayName = "#2294: a cross-invoice Duplicate identified only by its tracking number is withheld with the generic sentence, even when the number would resolve to an invoice the caller reads; no number is looked up")]
    [InlineData("DuplicateOfPriorSubmissionLine")]
    [InlineData("duplicate.line_prior_submission")]
    public async Task NumberOnlyDuplicate_IsWithheld_WhateverTheNumberWouldResolveTo(string reason)
    {
        var w = DuplicateWorld(EarlierNumber, null, reason);
        // Everything is readable: the current invoice, and the earlier invoice the number names.
        w.ReadableInvoices.UnionWith([EarlierInvoice, InvoiceId]);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        var duplicate = review.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP");
        ShouldBeWithheld(duplicate);
        duplicate.Message.Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
        OthersUntouched(review);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
        w.CallerPaths.Should().BeEmpty("a number-only Duplicate is judged by nothing: no read, no lookup");
    }

    [Theory(DisplayName = "119: an earlier invoice the caller cannot read withholds the whole Duplicate group (sentence, values, suggestion); other groups are untouched")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnreadableEarlierInvoice_IsWithheld(bool byId)
    {
        var w = DuplicateWorld(EarlierNumber, byId ? EarlierInvoice : null);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        var duplicate = review.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP");
        ShouldBeWithheld(duplicate);
        duplicate.Message.Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate, "a read-time withheld finding reads like a write-time one (K2)");
        OthersUntouched(review);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
    }

    // ── 119 round 5 (F4-1, ontology-r1): a Duplicate whose stored values give NO usable reference to the earlier invoice ──

    [Theory(DisplayName = "119 r5: a Duplicate whose stored values give no usable reference to the earlier invoice (a non-GUID id, duplicateOf.* values that are blank, a number only) is withheld, and the caller is asked nothing")]
    [InlineData("not-a-guid")]
    [InlineData("blank-values")]
    [InlineData("number-only")]
    public async Task UnusableReference_IsWithheld_AndAsksTheCallerNothing(string shape)
    {
        var duplicate = Finding(0, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 0);
        var values = (JsonArray)duplicate["valuesRead"]!;
        switch (shape)
        {
            case "not-a-guid":
                values.Add(Value("duplicateOf.invoiceId", "not-a-guid"));
                break;
            case "blank-values":
                // The Duplicate names its prior but the values are empty. (A finding with NO duplicateOf.* name at all is not a Duplicate to
                // the gate or to the Signal read: it quotes no earlier invoice and is returned as stored.)
                values.Add(Value("duplicateOf.invoiceId", ""));
                values.Add(Value("duplicateOf.trackingNumber", "  "));
                break;
            case "number-only":
                values.Add(Value("duplicateOf.trackingNumber", EarlierNumber));
                break;
        }

        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(Stored(
            [
                duplicate,
                Finding(1, "BILL-RATE", "rate.above_card", 0, ruleType: "RateCard"),
                Finding(2, "BILL-STAFF", "staffing.too_many", null, ruleType: "Staffing"),
            ],
            [
                Plan("BILL-DUP", LineId(1), DuplicateSentence, "DuplicateOfPriorSubmissionLine"),
                Plan("BILL-RATE", LineId(1), "The rate is above the agreed card.", "rate.above_card"),
                Plan("BILL-STAFF", InvoiceId, "Too many timekeepers.", "staffing.too_many"),
            ]));
        // Everything the caller could read is readable: only the missing reference withholds.
        w.ReadableInvoices.UnionWith([EarlierInvoice, InvoiceId]);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        var finding = review.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP");
        ShouldBeWithheld(finding);
        OthersUntouched(review);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
        JsonSerializer.Serialize(review).Should().NotContain("not-a-guid");
        w.CallerPaths.Should().BeEmpty("no usable reference: no caller read is made for the earlier-invoice check");
    }

    [Fact(DisplayName = "119: the whole (policy, subject) group is scrubbed and its sentence replaced even when the Duplicate is not the primary reason")]
    public async Task WithheldGroup_ReplacesTheSentence_WhenTheDuplicateIsNotPrimary()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(Stored(
            [Finding(0, "BILL-DUP", "other.reason", 0, ruleType: "Duplicate"), Finding(1, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice)],
            [Plan("BILL-DUP", LineId(1), DuplicateSentence, "other.reason", "DuplicateOfPriorSubmissionLine")]));

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;
        var findings = review.Lines.Single().Findings;

        ShouldBeWithheld(findings.Single(f => f.ReasonCode == "other.reason"), primary: true);
        ShouldBeWithheld(findings.Single(f => f.ReasonCode == "DuplicateOfPriorSubmissionLine"), primary: false);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
    }

    [Theory(DisplayName = "119: a fault in the caller's invoice read withholds; the page still loads")]
    [InlineData("500")]
    [InlineData("throw")]
    public async Task ReadFaults_Withhold(string failure)
    {
        var w = DuplicateWorld(EarlierNumber, EarlierInvoice);
        w.ReadableInvoices.Add(EarlierInvoice);
        switch (failure)
        {
            case "500":
                w.CallerFault = _ => Task.FromResult(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom"));
                break;
            case "throw":
                w.CallerFault = _ => Task.FromException<DataverseUserResponse>(new TimeoutException("read timed out"));
                break;
        }

        var review = await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review.Should().NotBeNull("the check never fails the page");
        ShouldBeWithheld(review!.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP"));
        OthersUntouched(review);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
    }

    [Fact(DisplayName = "119: a finding withheld at write time (no duplicateOf.*) is returned as stored and asks the caller nothing")]
    public async Task WriteTimeWithheld_StaysWithheld()
    {
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(Stored(
            [Finding(0, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, withheld: true)],
            [Plan("BILL-DUP", LineId(1), BillingRuleCatalogue.WithheldDuplicateTemplate, "DuplicateOfPriorSubmissionLine")]));

        var finding = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!.Lines.Single().Findings.Single();

        ShouldBeWithheld(finding);
        w.CallerPaths.Should().BeEmpty("no Duplicate quoting an earlier invoice: nothing to judge");
    }

    [Theory(DisplayName = "#2294: a same-invoice repeat (duplicate.line_same_invoice) keeps its detail when the caller reads its own invoice, and is withheld when not; no number is looked up")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameInvoiceRepeat_IsJudgedByTheCallersReadOfItsOwnInvoice(bool ownInvoiceReadable)
    {
        var w = DuplicateWorld("TRK-1", null, reason: "duplicate.line_same_invoice");
        w.ReadableInvoices.Add(EarlierInvoice);
        if (ownInvoiceReadable)
        {
            w.ReadableInvoices.Add(InvoiceId);
        }

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        var duplicate = review.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP");
        if (ownInvoiceReadable)
        {
            ShouldBeShown(duplicate, DuplicateSentence);
        }
        else
        {
            ShouldBeWithheld(duplicate);
        }

        OthersUntouched(review);
        Uri.UnescapeDataString(w.CallerPaths.Single()).Should().StartWith("sprk_invoices?").And.Contain(InvoiceId.ToString("D"),
            "the earlier invoice of a same-invoice repeat is the review's own invoice, read as the caller");
    }

    [Fact(DisplayName = "119 r2: two Duplicate groups, one readable and one not: only the unreadable one is withheld")]
    public async Task MixedReadability_WithholdsOnlyTheUnreadableGroup()
    {
        const string otherNumber = "F-080.1";
        const string otherSentence = "Invoice F-100, the line dated 2026-09-01 for J. Smith, duplicates F-080.1.";
        var w = new World();
        w.AddLine(1, InvoiceLineStatus.Flagged);
        w.AddLine(2, InvoiceLineStatus.Flagged);
        w.Submission = SubmissionRow(Stored(
            [
                Finding(0, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice),
                Finding(1, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 1, otherNumber, OtherEarlierInvoice),
            ],
            [
                Plan("BILL-DUP", LineId(1), DuplicateSentence, "DuplicateOfPriorSubmissionLine"),
                Plan("BILL-DUP", LineId(2), otherSentence, "DuplicateOfPriorSubmissionLine"),
            ]));
        w.ReadableInvoices.Add(EarlierInvoice);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        ShouldBeShown(review.Lines.Single(l => l.Sequence == 1).Findings.Single(), DuplicateSentence);
        ShouldBeWithheld(review.Lines.Single(l => l.Sequence == 2).Findings.Single());
        JsonSerializer.Serialize(review).Should().Contain(EarlierNumber, "the readable group is returned whole");
        ShouldNotLeak(review, otherNumber, OtherEarlierInvoice);
        w.CallerReads("sprk_invoices").Should().Be(1, "one check for both groups");
    }

    [Fact(DisplayName = "119 r3 (K4): a line whose sequence was edited never takes another index's plan sentence: plan and findings are keyed on the same line index")]
    public async Task EditedLineSequence_CannotPairOneIndexsFindingsWithAnotherIndexsSentence()
    {
        const string readableNumber = "F-080.1";
        const string readableSentence = "Invoice F-100, the line dated 2026-09-01 for J. Smith, duplicates F-080.1.";
        var w = new World();
        var indexZero = w.AddLine(1, InvoiceLineStatus.Flagged);
        var indexOne = w.AddLine(2, InvoiceLineStatus.Flagged);
        indexZero["sprk_linesequence"] = 2;
        indexOne["sprk_linesequence"] = 1;
        w.Submission = SubmissionRow(Stored(
            [
                Finding(0, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice),
                Finding(1, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 1, readableNumber, OtherEarlierInvoice),
            ],
            [
                Plan("BILL-DUP", InvoiceSubmissionPersister.LineIdFor(SubmissionId, 0), DuplicateSentence, "DuplicateOfPriorSubmissionLine"),
                Plan("BILL-DUP", InvoiceSubmissionPersister.LineIdFor(SubmissionId, 1), readableSentence, "DuplicateOfPriorSubmissionLine"),
            ]));
        w.ReadableInvoices.Add(OtherEarlierInvoice);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        ShouldBeWithheld(review.Lines.Single(l => l.Sequence == 1).Findings.Single());
        review.Lines.Single(l => l.Sequence == 1).Findings.Single().Message.Should().Be(EarlierInvoiceReadability.WithheldSentence);
        review.Lines.Single(l => l.Sequence == 2).Findings.Single().Message.Should().Be(readableSentence);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
        JsonSerializer.Serialize(review).Should().NotContain(DuplicateSentence);
    }

    [Fact(DisplayName = "119: one check per response: one caller invoice read whatever the number of Duplicates")]
    public async Task OneCheckPerResponse()
    {
        var w = new World();
        for (var i = 1; i <= 3; i++)
        {
            w.AddLine(i, InvoiceLineStatus.Flagged);
        }

        w.Submission = SubmissionRow(Stored(
            [
                Finding(0, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 0, EarlierNumber, EarlierInvoice),
                Finding(1, "BILL-DUP", "duplicate.line_same_invoice", 1, "TRK-1"),
                Finding(2, "BILL-DUP", "DuplicateOfPriorSubmissionLine", 2, "F-080.1", OtherEarlierInvoice),
                Finding(3, "BILL-DUPINV", "DuplicateInvoiceNumber", null, "F-080.1", OtherEarlierInvoice),
            ],
            [
                Plan("BILL-DUP", LineId(1), DuplicateSentence, "DuplicateOfPriorSubmissionLine"),
                Plan("BILL-DUP", LineId(2), DuplicateSentence, "duplicate.line_same_invoice"),
                Plan("BILL-DUP", LineId(3), DuplicateSentence, "DuplicateOfPriorSubmissionLine"),
                Plan("BILL-DUPINV", InvoiceId, "Invoice F-100 duplicates F-080.1.", "DuplicateInvoiceNumber"),
            ]));
        w.ReadableInvoices.UnionWith([EarlierInvoice, OtherEarlierInvoice, InvoiceId]);

        var review = (await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None))!;

        review.Lines.SelectMany(l => l.Findings).Concat(review.InvoiceFindings).Should().OnlyContain(f => !f.DetailWithheld);
        w.CallerReads("sprk_invoices").Should().Be(1);
        w.CallerPaths.Should().ContainSingle("ids and the own invoice of a same-invoice repeat share one read");
    }

    [Fact(DisplayName = "119: an exception escaping the check withholds every Duplicate group and the page still loads (never a 5xx)")]
    public async Task CheckThatThrows_WithholdsEveryDuplicate()
    {
        var w = DuplicateWorld(EarlierNumber, EarlierInvoice);
        w.ReadableInvoices.Add(EarlierInvoice);
        // A null response makes EarlierInvoiceReadability throw past its own guard (a NullReferenceException after the read): only the
        // read model's wrap stands between it and the route.
        w.CallerFault = _ => Task.FromResult<DataverseUserResponse>(null!);

        var review = await w.Service().GetAsync(InvoiceId, Caller, CancellationToken.None);

        review.Should().NotBeNull();
        ShouldBeWithheld(review!.Lines.Single().Findings.Single(f => f.PolicyCode == "BILL-DUP"));
        OthersUntouched(review);
        ShouldNotLeak(review, EarlierNumber, EarlierInvoice);
        // The read model's own wrap caught it (not a fault 157 handled itself): its Warning names the check.
        w.Logger.Verify(l => l.Log(
            LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("earlier-invoice check faulted (NullReferenceException)")),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    // Beyond the closed set, justified in the task notes: the contract says cancellation propagates through the wrap.
    [Fact(DisplayName = "119: the request's cancellation during the check propagates (it is not withheld-and-continued)")]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var w = DuplicateWorld(EarlierNumber, EarlierInvoice);
        w.CallerFault = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var act = () => w.Service().GetAsync(InvoiceId, Caller, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
