using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 095d-2a-1 (EB-D-189, option A; uac-r2's conditions): intake stores the four AI facts the re-run after Set firm needs (the
/// section totals with their confidences, the timekeeper summary names, a per-line "no date" flag and the observation quotes). Names and
/// quotes go only into the field-secured <c>sprk_findingsjson</c>; the line column carries the flag only; nothing is logged but counts. The
/// read rebuilt from the stored rows evaluates exactly as the document read did, and a submission stored without the facts refuses the
/// re-run fail-closed.
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    /// <summary>A name that is only on the invoice's timekeeper summary (no line bills it), so any other place it shows up is a leak.</summary>
    private const string SummaryOnlyName = "Zelda Quartermaine";

    /// <summary>The quoted text of the observation; the narrative holds it, the stored quote must be in the findings only.</summary>
    private const string Quote = "QUOTESENTINEL motion";

    private static readonly DateOnly RerunInvoiceDate = new(2026, 6, 1);

    private static ExtractedInvoice RerunRead(IReadOnlyList<ExtractedInvoiceLine> lines) => new()
    {
        Header = new ExtractedInvoiceHeader
        {
            FirmName = InvoiceSubmissionProcessorTests.F<string?>("Firm LLP"),
            FirmInvoiceNumber = InvoiceSubmissionProcessorTests.F<string?>("INV-100"),
            InvoiceDate = InvoiceSubmissionProcessorTests.F<DateOnly?>(RerunInvoiceDate),
            BillingPeriodStart = InvoiceSubmissionProcessorTests.F<DateOnly?>(new DateOnly(2026, 5, 1)),
            BillingPeriodEnd = InvoiceSubmissionProcessorTests.F<DateOnly?>(new DateOnly(2026, 5, 31)),
            ClientName = InvoiceSubmissionProcessorTests.F<string?>("Client"),
            MatterName = InvoiceSubmissionProcessorTests.F<string?>("Matter"),
            FirmMatterReference = InvoiceSubmissionProcessorTests.F<string?>(null, null),
            PurchaseOrderAsQuoted = InvoiceSubmissionProcessorTests.F<string?>(null, null),

            // Three decimals and a negative: the stored value is the exact number read, not the "0.00" header text.
            FeesTotal = InvoiceSubmissionProcessorTests.F<decimal?>(1500.125m, 0.91m),
            ExpensesTotal = InvoiceSubmissionProcessorTests.F<decimal?>(null, 0.4m),
            AdjustmentsTotal = InvoiceSubmissionProcessorTests.F<decimal?>(-25.5m, 0.7m),
            TotalDue = InvoiceSubmissionProcessorTests.F<decimal?>(1474.63m, 0.95m),
            Currency = InvoiceSubmissionProcessorTests.F<string?>("USD"),
        },
        Lines = lines,
        TimekeeperSummary =
        [
            SummaryRow("Jane Smith"),
            SummaryRow("   "),
            SummaryRow(SummaryOnlyName),
        ],
        PageCount = 3,
        AnalyzerId = "prebuilt-invoice-spaarke",
        ApiVersion = "2025-05-01",
    };

    private static ExtractedTimekeeperSummaryRow SummaryRow(string name) => new()
    {
        Name = InvoiceSubmissionProcessorTests.F<string?>(name),
        Title = InvoiceSubmissionProcessorTests.F<string?>("Partner"),
        Hours = InvoiceSubmissionProcessorTests.F<decimal?>(2m),
        Rate = InvoiceSubmissionProcessorTests.F<decimal?>(500m),
        Amount = InvoiceSubmissionProcessorTests.F<decimal?>(1000m),
    };

    /// <summary>Two fee lines: line 0 dated, line 1 read with NO date; both carry a block-billing observation quoting <see cref="Quote"/>.</summary>
    private static IReadOnlyList<ExtractedInvoiceLine> RerunLines() =>
    [
        InvoiceSubmissionProcessorTests.FeeLine() with { Narrative = InvoiceSubmissionProcessorTests.F<string?>("Drafted the " + Quote + ".") },
        InvoiceSubmissionProcessorTests.FeeLine() with
        {
            Date = InvoiceSubmissionProcessorTests.F<DateOnly?>(null, 0.2m),
            Narrative = InvoiceSubmissionProcessorTests.F<string?>("Revised the " + Quote + "."),
            Rate = InvoiceSubmissionProcessorTests.F<decimal?>(500m) with { Repair = ExtractedValueRepair.RateFromTimekeeperSummary, ReturnedValue = null },
        },
    ];

    private static IInvoiceAi ReadingAi(ExtractedInvoice read)
    {
        var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
        ai.Setup(a => a.ReadInvoiceAsync(It.IsAny<InvoiceReadRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(read);
        return ai.Object;
    }

    /// <summary>The intake of a firmless submission (095c-3) over <see cref="RerunRead"/>, coded with a block-billing observation per line.</summary>
    private static ProcessorHarness NoFirmIntake(params BillingRuleCandidate[] rules)
    {
        var read = RerunRead(RerunLines());
        var harness = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = rules, Ai = ReadingAi(read), Lines = read.Lines };
        harness.Row.Attributes.Remove("sprk_vendororg");
        harness.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.9m, QuotedText = Quote }];
        return harness;
    }

    /// <summary>Persists a firmless intake and returns the store, the stored line rows (in line order) and the stored findings JSON text.</summary>
    private static async Task<(Store Store, List<Entity> Lines, string Findings)> PersistNoFirmIntakeAsync(CapturingLogger? logger = null)
    {
        var outcome = await NoFirmIntake().RunAsync();
        var store = NoFirmStore(logger is null ? null : new Store { Logger = logger });
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var requests = store.Transactions.Single();
        var lines = requests.Select(Target).Where(e => e.LogicalName == "sprk_billingevent").ToList();
        return (store, lines, Target(requests[^1]).GetAttributeValue<string>("sprk_findingsjson"));
    }

    private static JsonElement Provenance(string findings) => JsonDocument.Parse(findings).RootElement.GetProperty(ExtractionProvenance.Member);

    // ── (a) the section totals ──

    [Fact(DisplayName = "095d-2a-1 (a): the section totals are stored with their confidences, as exact numbers, in the findings provenance; never on a line")]
    public async Task SectionTotals_AreStoredInTheFindings_WithTheirConfidences()
    {
        var (_, lines, findings) = await PersistNoFirmIntakeAsync();

        var totals = Provenance(findings).GetProperty(ExtractionProvenance.SectionTotalsMember);
        totals.GetProperty(ResolutionFieldNames.FeesTotal).GetProperty("value").GetDecimal().Should().Be(1500.125m, "the exact number read, not rounded text");
        totals.GetProperty(ResolutionFieldNames.FeesTotal).GetProperty("confidence").GetDecimal().Should().Be(0.91m);
        totals.GetProperty(ResolutionFieldNames.AdjustmentsTotal).GetProperty("value").GetDecimal().Should().Be(-25.5m);
        totals.GetProperty(ResolutionFieldNames.AdjustmentsTotal).GetProperty("confidence").GetDecimal().Should().Be(0.7m);

        // Must-not-fire: a total the document does not show stays null (with the analyzer's confidence that it is absent), never 0.
        totals.GetProperty(ResolutionFieldNames.ExpensesTotal).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        totals.GetProperty(ResolutionFieldNames.ExpensesTotal).GetProperty("confidence").GetDecimal().Should().Be(0.4m);
        lines.Should().OnlyContain(l => !l.GetAttributeValue<string>("sprk_extractionconfidence").Contains(ExtractionProvenance.SectionTotalsMember));
    }

    // ── (b) the timekeeper summary names ──

    [Fact(DisplayName = "095d-2a-1 (b): the timekeeper summary names are stored in the findings only (blank names dropped); no line column holds them")]
    public async Task TimekeeperSummaryNames_AreStoredInTheFindingsOnly()
    {
        var (_, lines, findings) = await PersistNoFirmIntakeAsync();

        Provenance(findings).GetProperty(ExtractionProvenance.TimekeeperSummaryMember).EnumerateArray().Select(n => n.GetString())
            .Should().Equal("Jane Smith", SummaryOnlyName);

        // Must-not-fire: no line row, in any column, holds a name that is only on the summary (uac-r2 condition 1).
        lines.Should().OnlyContain(l => !l.Attributes.Values.OfType<string>().Any(v => v.Contains(SummaryOnlyName, StringComparison.Ordinal)));
    }

    // ── (c) the per-line "no date" flag ──

    [Fact(DisplayName = "095d-2a-1 (c): a line read with no date is flagged dateMissing on its extraction JSON (its event date is the invoice date); a dated line is not")]
    public async Task ALineReadWithNoDate_IsFlagged_ADatedLineIsNot()
    {
        var (_, lines, _) = await PersistNoFirmIntakeAsync();

        bool Flag(Entity line) => JsonNode.Parse(line.GetAttributeValue<string>("sprk_extractionconfidence"))![ExtractionProvenance.LineDateMissingMember]!.GetValue<bool>();

        Flag(lines[1]).Should().BeTrue();
        lines[1].GetAttributeValue<DateTime>("sprk_eventdate").Should().Be(RerunInvoiceDate.ToDateTime(TimeOnly.MinValue), "FR-04: the column holds the invoice date");
        Flag(lines[0]).Should().BeFalse("must-not-fire: the line was read with its date");
    }

    // ── (d) the observation quotes ──

    [Fact(DisplayName = "095d-2a-1 (d): the observation quotes are stored in the findings only; the line keeps the observation's type and confidence, never its text")]
    public async Task ObservationQuotes_AreStoredInTheFindingsOnly()
    {
        var (_, lines, findings) = await PersistNoFirmIntakeAsync();

        var quotes = Provenance(findings).GetProperty(ExtractionProvenance.ObservationsMember).EnumerateArray().ToList();
        quotes.Select(q => (q.GetProperty("line").GetInt32(), q.GetProperty("type").GetString(), q.GetProperty("quotedText").GetString()))
            .Should().Equal((0, "BlockBilling", Quote), (1, "BlockBilling", Quote));

        // Must-not-fire: the line column carries numbers and flags only (uac-r2 condition 1).
        foreach (var line in lines)
        {
            var extraction = line.GetAttributeValue<string>("sprk_extractionconfidence");
            extraction.Should().NotContain("QUOTESENTINEL");
            JsonNode.Parse(extraction)!["coding"]!["observations"]!.AsArray().Should().ContainSingle()
                .Which!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("type", "confidence");
        }
    }

    // ── never logged ──

    [Fact(DisplayName = "095d-2a-1: no log line of intake, the rebuild or the re-run coding carries a stored name or quote (counts only, ADR-015)")]
    public async Task TheStoredFacts_AreNeverLogged()
    {
        var logger = new CapturingLogger();
        var (_, lines, findings) = await PersistNoFirmIntakeAsync(logger);
        var rebuilt = StoredSubmissionRead.Rebuild(Submission, findings, lines, logger);
        rebuilt.IsRerunnable.Should().BeTrue("the setup");
        var coding = new RerunLineCoding(new InvoiceSubmissionProcessorTests.FakeCoding(), (_, _) => Task.FromResult<ResolvedCodeSet?>(null), rebuilt.Coding!, logger);
        await coding.CodeLinesAsync(InvoiceSubmissionProcessor.BuildCodingRequest(FirmSnapshot(), rebuilt.Read!));

        logger.Entries.Should().NotBeEmpty("the setup: the steps log");
        logger.Entries.Should().Contain(e => e.Message.Contains("2 summary names", StringComparison.Ordinal), "must-fire: the count is logged");
        logger.Entries.Should().NotContain(e => e.Message.Contains(SummaryOnlyName, StringComparison.Ordinal)
            || e.Message.Contains("Jane Smith", StringComparison.Ordinal) || e.Message.Contains("QUOTESENTINEL", StringComparison.Ordinal));
    }

    // ── the rebuild ──

    private static SubmissionSnapshot FirmSnapshot()
    {
        var withFirm = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal };
        return SubmissionSnapshot.Read(withFirm.Row).Snapshot!;
    }

    [Fact(DisplayName = "095d-2a-1: the read rebuilt from the stored rows (no document read) evaluates with the firm exactly as the document read does, quotes and no-date lines included")]
    public async Task TheRebuiltRead_EvaluatesAsTheDocumentReadDoes_WithoutReadingTheDocument()
    {
        BillingRuleCandidate[] rules = [Version("BILL-AI", RuleType.AiObservation), Version("BILL-TK", RuleType.ApprovedTimekeeper)];
        var (_, lines, findings) = await PersistNoFirmIntakeAsync();

        var rebuilt = StoredSubmissionRead.Rebuild(Submission, findings, lines, new CapturingLogger());
        rebuilt.IsRerunnable.Should().BeTrue();
        rebuilt.Read!.Lines[1].Date.Value.Should().BeNull("the stored flag restores the missing date");
        rebuilt.Read.Lines[0].Date.Value.Should().Be(new DateOnly(2026, 5, 20));
        rebuilt.Read.Lines[1].Rate.Repair.Should().Be(ExtractedValueRepair.RateFromTimekeeperSummary);
        rebuilt.Read.Header.FeesTotal.Value.Should().Be(1500.125m);
        rebuilt.Read.TimekeeperSummary.Select(r => r.Name.Value).Should().Equal("Jane Smith", SummaryOnlyName);

        // The document read, with the firm.
        var direct = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = rules, Ai = ReadingAi(RerunRead(RerunLines())), Lines = RerunLines() };
        direct.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.9m, QuotedText = Quote }];
        var expected = await direct.RunAsync();

        // The re-run: the rebuilt read through the same steps, with an AI that fails any call (no ReadInvoiceAsync), and the stored coding.
        var rerun = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = rules, Ai = new Mock<IInvoiceAi>(MockBehavior.Strict).Object };
        var coding = new RerunLineCoding(rerun.Coding, (_, _) => Task.FromResult<ResolvedCodeSet?>(null), rebuilt.Coding!, new CapturingLogger());
        var actual = await rerun.Build().EvaluateFromReadAsync(FirmSnapshot(), rebuilt.Read, finalDelivery: false, coding);

        rerun.DocumentLoads.Should().Be(0);
        rerun.Reads.Should().Be(0);
        rerun.Coding.Request.Should().BeNull("no line's code set changed, so nothing was sent to the coding service");
        actual.Kind.Should().Be(expected.Kind);
        actual.ReasonCode.Should().Be(expected.ReasonCode);
        expected.Evaluation!.Findings.Should().Contain(f => f.ValuesRead.Any(v => v.Value == Quote), "the setup: the AiObservation rule fires and reads the quote");
        JsonSerializer.Serialize(actual.EvaluationRequest).Should().Be(JsonSerializer.Serialize(expected.EvaluationRequest));
        JsonSerializer.Serialize(actual.Evaluation!.Findings).Should().Be(JsonSerializer.Serialize(expected.Evaluation.Findings));
        JsonSerializer.Serialize(actual.PipelineFindings).Should().Be(JsonSerializer.Serialize(expected.PipelineFindings));
        JsonSerializer.Serialize(rerun.Resolution.Input).Should().Be(JsonSerializer.Serialize(direct.Resolution.Input), "the resolution sees the same totals, names and dates");

        // The persister's builders give the same stored copy (the invoice name aside: the firm name is not a stored fact and decides no rule).
        static string Stored(SubmissionEvaluationOutcome outcome)
        {
            var root = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId))!.AsObject();
            root.Remove("invoiceName");
            return root.ToJsonString();
        }

        Stored(actual).Should().Be(Stored(expected));
    }

    // ── fail closed: a submission stored without the facts ──

    /// <summary>A stored copy as intake wrote it before task 095d-2a-1: no re-run member on the findings provenance, no flag on the lines.</summary>
    private static (string Findings, List<Entity> Lines) AsStoredBefore(string findings, List<Entity> lines, string? only = null)
    {
        var root = JsonNode.Parse(findings)!.AsObject();
        var provenance = root[ExtractionProvenance.Member]!.AsObject();
        foreach (var member in new[] { ExtractionProvenance.SectionTotalsMember, ExtractionProvenance.TimekeeperSummaryMember, ExtractionProvenance.ObservationsMember })
        {
            if (only is null || only == member)
            {
                provenance.Remove(member);
            }
        }

        var stripped = lines.Select(l =>
        {
            var copy = new Entity(l.LogicalName, l.Id);
            foreach (var (key, value) in l.Attributes)
            {
                copy[key] = value;
            }

            if (only is null || only == ExtractionProvenance.LineDateMissingMember)
            {
                var extraction = JsonNode.Parse(l.GetAttributeValue<string>("sprk_extractionconfidence"))!.AsObject();
                extraction.Remove(ExtractionProvenance.LineDateMissingMember);
                copy["sprk_extractionconfidence"] = extraction.ToJsonString();
            }

            return copy;
        }).ToList();
        return (root.ToJsonString(), stripped);
    }

    [Theory(DisplayName = "095d-2a-1 SEEDED: a submission stored without the re-run facts (any one of them) is not re-runnable: a typed refusal, nothing rebuilt, logged Permanent=true")]
    [InlineData(null)]
    [InlineData(ExtractionProvenance.SectionTotalsMember)]
    [InlineData(ExtractionProvenance.TimekeeperSummaryMember)]
    [InlineData(ExtractionProvenance.ObservationsMember)]
    [InlineData(ExtractionProvenance.LineDateMissingMember)]
    public async Task ASubmissionStoredWithoutTheFacts_RefusesTheRerun_FailClosed(string? missing)
    {
        var (store, lines, findings) = await PersistNoFirmIntakeAsync();
        var transactions = store.Transactions.Count;
        var (older, olderLines) = AsStoredBefore(findings, lines, missing);
        var logger = new CapturingLogger();

        var result = StoredSubmissionRead.Rebuild(Submission, older, olderLines, logger);

        result.IsRerunnable.Should().BeFalse();
        result.RefusalReason.Should().Be(StoredSubmissionRead.FactsMissing);
        result.Read.Should().BeNull("no fact is guessed");
        result.Coding.Should().BeNull();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message
            .Should().Contain("Permanent=True").And.Contain(StoredSubmissionRead.FactsMissing);
        store.Transactions.Should().HaveCount(transactions, "the rebuild writes nothing, so the hold stays");

        // Must-not-fire: the same submission as stored now is re-runnable, and logs no refusal.
        var control = new CapturingLogger();
        StoredSubmissionRead.Rebuild(Submission, findings, lines, control).IsRerunnable.Should().BeTrue();
        control.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "095d-2a-1: stored rows that cannot be read back consistently (a line sequence gap, a quote without its observation) are not re-runnable")]
    public async Task InconsistentStoredRows_AreNotRerunnable()
    {
        var (_, lines, findings) = await PersistNoFirmIntakeAsync();

        // A gap in the line sequence (a line row missing).
        StoredSubmissionRead.Rebuild(Submission, findings, [lines[1]], new CapturingLogger()).RefusalReason.Should().Be(StoredSubmissionRead.StoredRowsInconsistent);

        // A quote with no observation on its line.
        var root = JsonNode.Parse(findings)!.AsObject();
        root[ExtractionProvenance.Member]![ExtractionProvenance.ObservationsMember]!.AsArray()
            .Add(new JsonObject { ["line"] = 0, ["type"] = "VagueDescription", ["quotedText"] = "x" });
        StoredSubmissionRead.Rebuild(Submission, root.ToJsonString(), lines, new CapturingLogger()).RefusalReason.Should().Be(StoredSubmissionRead.StoredRowsInconsistent);
    }
}
