using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
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
        public readonly List<QueryExpression> Queries = [];
        public readonly Rights Access = new();
        public decimal Threshold = 0.90m;
        public bool SettingsFault;
        public readonly Mock<IGenericEntityService> Entities = new(MockBehavior.Strict);

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
                        "sprk_invoicesubmission" => new EntityCollection(Submission is null ? [] : [Submission]),
                        "sprk_decisionrecord" => new EntityCollection(History.ToList()),
                        "sprk_timekeeper" => new EntityCollection(Roster.ToList()),
                        _ => throw new InvalidOperationException($"Unexpected read of {query.EntityName}"),
                    };
                });
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
            return new InvoiceReviewReadService(Entities.Object, authorization, settings.Object, NullLogger<InvoiceReviewReadService>.Instance);
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

    private static Guid LineId(int sequence) => Guid.Parse($"10000000-0000-4000-8000-{sequence:D12}");

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
        w.Queries.Should().HaveCount(5, "invoice, lines, submission, history, roster - whatever the line count");
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
}
