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
/// Ebilling-r1 task 095d-2a-1 (EB-D-189, option A; uac-r2's conditions): an intake held for its firm stores the AI facts the re-run after Set
/// firm needs (the section totals and total due with their confidences, the timekeeper summary names, the observation quotes, the line count
/// and each line's exact numbers; every line also carries a "no date" flag). An intake with a firm stores none of them. Names and quotes go
/// only into the field-secured <c>sprk_findingsjson</c>; the line column carries numbers and flags; nothing is logged but counts. The read
/// rebuilt from the stored rows (rounded as Dataverse stores them) evaluates exactly as the document read did, and stored rows that do not
/// hold the facts refuse the re-run fail-closed.
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    /// <summary>A name that is only on the invoice's timekeeper summary (no line bills it), so any other place it shows up is a leak.</summary>
    private const string SummaryOnlyName = "Zelda Quartermaine";

    /// <summary>The quoted text of the observation; the narrative holds it, the stored quote must be in the findings only.</summary>
    private const string Quote = "QUOTESENTINEL motion";

    private static readonly DateOnly RerunInvoiceDate = new(2026, 6, 1);

    private static readonly IReadOnlySet<(Guid? LineId, string Field)> NoneVerified = new HashSet<(Guid? LineId, string Field)>();

    private static ExtractedInvoice RerunRead(IReadOnlyList<ExtractedInvoiceLine> lines, decimal currencyConfidence = 0.99m) => new()
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
            TotalDue = InvoiceSubmissionProcessorTests.F<decimal?>(1474.625m, 0.95m),
            Currency = InvoiceSubmissionProcessorTests.F<string?>("USD", currencyConfidence),
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

    /// <summary>
    /// Four lines, each with a block-billing observation quoting <see cref="Quote"/>: line 0 dated; line 1 read with NO date and a repaired rate;
    /// line 2 a fee of 0.125 hours; line 3 an expense of 100 at 0.655, billed 66.00. Lines 2 and 3 carry numbers the 2-decimal columns round.
    /// </summary>
    private static IReadOnlyList<ExtractedInvoiceLine> RerunLines() =>
    [
        InvoiceSubmissionProcessorTests.FeeLine() with { Narrative = InvoiceSubmissionProcessorTests.F<string?>("Drafted the " + Quote + ".") },
        InvoiceSubmissionProcessorTests.FeeLine() with
        {
            RowIndex = 1,
            Date = InvoiceSubmissionProcessorTests.F<DateOnly?>(null, 0.2m),
            Narrative = InvoiceSubmissionProcessorTests.F<string?>("Revised the " + Quote + "."),
            Rate = InvoiceSubmissionProcessorTests.F<decimal?>(500m) with { Repair = ExtractedValueRepair.RateFromTimekeeperSummary, ReturnedValue = null },
        },
        InvoiceSubmissionProcessorTests.FeeLine(hours: 0.125m, amount: 62.5m) with
        {
            RowIndex = 2,
            Narrative = InvoiceSubmissionProcessorTests.F<string?>("Reviewed the " + Quote + "."),
        },
        InvoiceSubmissionProcessorTests.ExpenseLine("E101", amount: 66.00m) with
        {
            RowIndex = 0,
            Quantity = InvoiceSubmissionProcessorTests.F<decimal?>(100m),
            UnitCost = InvoiceSubmissionProcessorTests.F<decimal?>(0.655m),
            Narrative = InvoiceSubmissionProcessorTests.F<string?>("Copies of the " + Quote + "."),
        },
    ];

    private static IInvoiceAi ReadingAi(ExtractedInvoice read)
    {
        var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
        ai.Setup(a => a.ReadInvoiceAsync(It.IsAny<InvoiceReadRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(read);
        return ai.Object;
    }

    private static readonly InvoiceLineObservation BlockBillingQuote =
        new() { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.9m, QuotedText = Quote };

    /// <summary>The intake of a firmless submission (095c-3) over <see cref="RerunRead"/>, coded with a block-billing observation per line.</summary>
    private static ProcessorHarness NoFirmIntake(IReadOnlyList<ExtractedInvoiceLine>? lines = null, ILogger? logger = null, decimal currencyConfidence = 0.99m)
    {
        var read = RerunRead(lines ?? RerunLines(), currencyConfidence);
        var harness = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Ai = ReadingAi(read), Lines = read.Lines, Logger = logger };
        harness.Row.Attributes.Remove("sprk_vendororg");
        harness.Coding.Observations = [BlockBillingQuote];
        return harness;
    }

    /// <summary>What a firmless intake stored: the line rows (in line order, rounded as Dataverse stores them), the findings JSON, the invoice.</summary>
    private sealed record StoredIntake(Store Store, List<Entity> Lines, string Findings, Entity Invoice);

    /// <summary>Persists a firmless intake.</summary>
    private static async Task<StoredIntake> PersistNoFirmIntakeAsync(
        CapturingLogger? logger = null, IReadOnlyList<ExtractedInvoiceLine>? lines = null, decimal currencyConfidence = 0.99m)
    {
        var outcome = await NoFirmIntake(lines, logger, currencyConfidence).RunAsync();
        var store = NoFirmStore(logger is null ? null : new Store { Logger = logger });
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var requests = store.Transactions.Single();
        var rows = requests.Select(Target).ToList();
        return new StoredIntake(
            store,
            AsDataverseStores(rows.Where(e => e.LogicalName == "sprk_billingevent")),
            rows.Single(e => e.LogicalName == "sprk_invoicesubmission").GetAttributeValue<string>("sprk_findingsjson"),
            rows.Single(e => e.LogicalName == "sprk_invoice"));
    }

    /// <summary>The rows as Dataverse returns them: the decimal and currency columns hold two decimals (review L1: the rebuild must not trust them).</summary>
    private static List<Entity> AsDataverseStores(IEnumerable<Entity> rows) => rows.Select(row =>
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var (key, value) in row.Attributes)
        {
            copy[key] = value switch
            {
                decimal number => Math.Round(number, 2, MidpointRounding.AwayFromZero),
                Money money => new Money(Math.Round(money.Value, 2, MidpointRounding.AwayFromZero)),
                _ => value,
            };
        }

        return copy;
    }).ToList();

    private static Entity Copy(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var (key, value) in row.Attributes)
        {
            copy[key] = value;
        }

        return copy;
    }

    private static StoredSubmissionRead.Result Rebuild(
        StoredIntake intake, string? findings = null, IEnumerable<Entity>? lines = null, Entity? invoice = null,
        IReadOnlySet<(Guid? LineId, string Field)>? verified = null, ILogger? logger = null) =>
        StoredSubmissionRead.Rebuild(Submission, findings ?? intake.Findings, invoice ?? intake.Invoice, lines ?? intake.Lines, verified ?? NoneVerified, logger ?? new CapturingLogger());

    private static JsonElement Provenance(string findings) => JsonDocument.Parse(findings).RootElement.GetProperty(ExtractionProvenance.Member);

    private static JsonObject LineFields(Entity line) => JsonNode.Parse(line.GetAttributeValue<string>("sprk_extractionconfidence"))!["fields"]!.AsObject();

    // ── (a) the section totals, the total due and the line count ──

    [Fact(DisplayName = "095d-2a-1 (a): the section totals and total due are stored with their confidences, as exact numbers, in the findings provenance, with the line count; never on a line")]
    public async Task SectionTotals_AreStoredInTheFindings_WithTheirConfidences()
    {
        var intake = await PersistNoFirmIntakeAsync();

        var provenance = Provenance(intake.Findings);
        var totals = provenance.GetProperty(ExtractionProvenance.SectionTotalsMember);
        totals.GetProperty(ResolutionFieldNames.FeesTotal).GetProperty("value").GetDecimal().Should().Be(1500.125m, "the exact number read, not rounded text");
        totals.GetProperty(ResolutionFieldNames.FeesTotal).GetProperty("confidence").GetDecimal().Should().Be(0.91m);
        totals.GetProperty(ResolutionFieldNames.AdjustmentsTotal).GetProperty("value").GetDecimal().Should().Be(-25.5m);
        totals.GetProperty(ResolutionFieldNames.AdjustmentsTotal).GetProperty("confidence").GetDecimal().Should().Be(0.7m);
        totals.GetProperty(ResolutionFieldNames.TotalDue).GetProperty("value").GetDecimal().Should().Be(1474.625m, "review L4: the total due as a number, not the header's 0.00 text");
        provenance.GetProperty(ExtractionProvenance.LineCountMember).GetInt32().Should().Be(4);

        // Must-not-fire: a total the document does not show stays null (with the analyzer's confidence that it is absent), never 0.
        totals.GetProperty(ResolutionFieldNames.ExpensesTotal).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        totals.GetProperty(ResolutionFieldNames.ExpensesTotal).GetProperty("confidence").GetDecimal().Should().Be(0.4m);
        intake.Lines.Should().OnlyContain(l => !l.GetAttributeValue<string>("sprk_extractionconfidence").Contains(ExtractionProvenance.SectionTotalsMember));
    }

    // ── (b) the timekeeper summary names ──

    [Fact(DisplayName = "095d-2a-1 (b): the timekeeper summary names are stored in the findings only (blank names dropped); no line column holds them")]
    public async Task TimekeeperSummaryNames_AreStoredInTheFindingsOnly()
    {
        var intake = await PersistNoFirmIntakeAsync();

        Provenance(intake.Findings).GetProperty(ExtractionProvenance.TimekeeperSummaryMember).EnumerateArray().Select(n => n.GetString())
            .Should().Equal("Jane Smith", SummaryOnlyName);

        // Must-not-fire: no line row, in any column, holds a name that is only on the summary (uac-r2 condition 1).
        intake.Lines.Should().OnlyContain(l => !l.Attributes.Values.OfType<string>().Any(v => v.Contains(SummaryOnlyName, StringComparison.Ordinal)));
    }

    // ── (c) the per-line "no date" flag ──

    [Fact(DisplayName = "095d-2a-1 (c): a line read with no date is flagged dateMissing on its extraction JSON (its event date is the invoice date); a dated line is not")]
    public async Task ALineReadWithNoDate_IsFlagged_ADatedLineIsNot()
    {
        var intake = await PersistNoFirmIntakeAsync();

        bool Flag(Entity line) => JsonNode.Parse(line.GetAttributeValue<string>("sprk_extractionconfidence"))![ExtractionProvenance.LineDateMissingMember]!.GetValue<bool>();

        Flag(intake.Lines[1]).Should().BeTrue();
        intake.Lines[1].GetAttributeValue<DateTime>("sprk_eventdate").Should().Be(RerunInvoiceDate.ToDateTime(TimeOnly.MinValue), "FR-04: the column holds the invoice date");
        Flag(intake.Lines[0]).Should().BeFalse("must-not-fire: the line was read with its date");
    }

    // ── (d) the observation quotes ──

    [Fact(DisplayName = "095d-2a-1 (d): the observation quotes are stored in the findings only; the line keeps the observation's type and confidence, never its text")]
    public async Task ObservationQuotes_AreStoredInTheFindingsOnly()
    {
        var intake = await PersistNoFirmIntakeAsync();

        var quotes = Provenance(intake.Findings).GetProperty(ExtractionProvenance.ObservationsMember).EnumerateArray().ToList();
        quotes.Select(q => (q.GetProperty("line").GetInt32(), q.GetProperty("type").GetString(), q.GetProperty("quotedText").GetString()))
            .Should().Equal((0, "BlockBilling", Quote), (1, "BlockBilling", Quote), (2, "BlockBilling", Quote), (3, "BlockBilling", Quote));

        // Must-not-fire: the line column carries numbers and flags only (uac-r2 condition 1).
        foreach (var line in intake.Lines)
        {
            var extraction = line.GetAttributeValue<string>("sprk_extractionconfidence");
            extraction.Should().NotContain("QUOTESENTINEL");
            JsonNode.Parse(extraction)!["coding"]!["observations"]!.AsArray().Should().ContainSingle()
                .Which!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("type", "confidence");
        }
    }

    // ── (e) the exact numbers (review L1) ──

    [Fact(DisplayName = "095d-2a-1 (e): each line's exact numbers are stored in its extraction JSON (numbers only), so the 2-decimal columns are not what the re-run judges")]
    public async Task ExactNumbers_AreStoredOnTheLines()
    {
        var intake = await PersistNoFirmIntakeAsync();

        LineFields(intake.Lines[2])[ResolutionFieldNames.Hours]![ExtractionProvenance.ExactMember]!.GetValue<decimal>().Should().Be(0.125m);
        intake.Lines[2].GetAttributeValue<decimal>("sprk_quantity").Should().Be(0.13m, "the setup: the column rounds");
        LineFields(intake.Lines[3])[ResolutionFieldNames.UnitCost]![ExtractionProvenance.ExactMember]!.GetValue<decimal>().Should().Be(0.655m);
        LineFields(intake.Lines[3])[ResolutionFieldNames.Quantity]![ExtractionProvenance.ExactMember]!.GetValue<decimal>().Should().Be(100m);

        // A field the line type does not have is stored as a JSON null, never a guess.
        LineFields(intake.Lines[3])[ResolutionFieldNames.Hours]![ExtractionProvenance.ExactMember].Should().BeNull();
        foreach (var line in intake.Lines)
        {
            foreach (var name in ExtractionProvenance.ExactLineFieldNames)
            {
                LineFields(line)[name]!.AsObject().ContainsKey(ExtractionProvenance.ExactMember).Should().BeTrue();
                LineFields(line)[name]![ExtractionProvenance.ExactMember]?.GetValueKind().Should().Be(JsonValueKind.Number, "numbers only (uac-r2 condition 1)");
            }
        }
    }

    // ── review L6: only an intake held for its firm stores them ──

    [Fact(DisplayName = "095d-2a-1 (L6) must-not-fire: an intake with a firm stores none of the re-run facts (no totals, names, quotes, line count or exact numbers)")]
    public async Task AnIntakeWithAFirm_StoresNoRerunFacts()
    {
        var read = RerunRead(RerunLines());
        var harness = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Ai = ReadingAi(read), Lines = read.Lines };
        harness.Coding.Observations = [BlockBillingQuote];
        var outcome = await harness.RunAsync();
        outcome.Submission!.FirmId.Should().NotBeNull("the setup");
        var store = new Store();
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var rows = store.Transactions.Single().Select(Target).ToList();
        var findings = rows.Single(e => e.LogicalName == "sprk_invoicesubmission").GetAttributeValue<string>("sprk_findingsjson");

        var provenance = Provenance(findings);
        foreach (var member in new[] { ExtractionProvenance.SectionTotalsMember, ExtractionProvenance.TimekeeperSummaryMember, ExtractionProvenance.ObservationsMember, ExtractionProvenance.LineCountMember })
        {
            provenance.TryGetProperty(member, out _).Should().BeFalse(member);
        }

        findings.Should().NotContain(SummaryOnlyName).And.NotContain("QUOTESENTINEL");
        var lines = rows.Where(e => e.LogicalName == "sprk_billingevent").ToList();
        lines.Should().HaveCount(4);
        lines.Should().OnlyContain(l => !l.GetAttributeValue<string>("sprk_extractionconfidence").Contains("\"" + ExtractionProvenance.ExactMember + "\""));

        // The flag is on every line, with a firm or without (the simpler rule; a boolean).
        lines.Should().OnlyContain(l => l.GetAttributeValue<string>("sprk_extractionconfidence").Contains(ExtractionProvenance.LineDateMissingMember));

        // Must-fire control: the same read without a firm stores them.
        var firmless = await PersistNoFirmIntakeAsync();
        Provenance(firmless.Findings).TryGetProperty(ExtractionProvenance.LineCountMember, out _).Should().BeTrue();
        ExtractionProvenance.ParseRerunFacts(findings).Should().BeNull("a firm intake cannot be re-run, so the rebuild refuses it as facts missing");
    }

    // ── never logged, never printed ──

    [Fact(DisplayName = "095d-2a-1: no log line of intake, the rebuild, the re-run coding or the re-run evaluation (processor, rejection rule, rules) carries a stored name or quote (counts only, ADR-015)")]
    public async Task TheStoredFacts_AreNeverLogged()
    {
        var logger = new CapturingLogger();
        var intake = await PersistNoFirmIntakeAsync(logger);
        var rebuilt = Rebuild(intake, logger: logger);
        rebuilt.IsRerunnable.Should().BeTrue("the setup");

        // The re-run evaluation, with every step's logger (the processor, the rejection rule, the rule evaluator) writing to the same logger.
        BillingRuleCandidate[] rules = [Version("BILL-AI", RuleType.AiObservation), Version("BILL-TK", RuleType.ApprovedTimekeeper)];
        var rerun = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = rules, Ai = new Mock<IInvoiceAi>(MockBehavior.Strict).Object, Logger = logger };
        var coding = new RerunLineCoding(rerun.Coding, (_, _) => Task.FromResult<ResolvedCodeSet?>(null), rebuilt.Coding!, logger);
        var outcome = await rerun.Build().EvaluateFromReadAsync(FirmSnapshot(), rebuilt.Read!, finalDelivery: false, coding);
        outcome.Evaluation!.Findings.Should().Contain(f => f.ValuesRead.Any(v => v.Value == Quote), "the setup: the quote is in the evaluation");

        logger.Entries.Should().NotBeEmpty("the setup: the steps log");
        logger.Entries.Should().Contain(e => e.Message.Contains("2 summary names", StringComparison.Ordinal), "must-fire: the count is logged");
        logger.Entries.Should().Contain(e => e.Message.Contains("Re-run coding", StringComparison.Ordinal), "the setup: the re-run coding logged");
        logger.Entries.Should().NotContain(e => e.Message.Contains(SummaryOnlyName, StringComparison.Ordinal)
            || e.Message.Contains("Jane Smith", StringComparison.Ordinal) || e.Message.Contains("QUOTESENTINEL", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "095d-2a-1 (X4): ToString() of the new records never prints a stored name or quote")]
    public async Task TheNewRecords_NeverPrintANameOrAQuote()
    {
        var intake = await PersistNoFirmIntakeAsync();
        var facts = ExtractionProvenance.ParseRerunFacts(intake.Findings)!;
        var rebuilt = Rebuild(intake);

        var quote = new ExtractionProvenance.ObservationQuote(0, InvoiceLineObservationType.BlockBilling, Quote);
        quote.ToString().Should().NotContain("QUOTESENTINEL").And.Contain("Line = 0").And.Contain("20 characters");
        quote.QuotedText.Should().Be(Quote, "the value itself is unchanged");

        foreach (var printed in new[] { facts.ToString(), facts.Observations[0].ToString(), rebuilt.ToString(), rebuilt.Coding!.ToString(), rebuilt.Read!.ToString() })
        {
            printed.Should().NotContain("QUOTESENTINEL").And.NotContain(SummaryOnlyName).And.NotContain("Jane Smith");
        }

        facts.ToString().Should().Contain("TimekeeperSummaryNames = 2").And.Contain("Observations = 4").And.Contain("LineCount = 4");
    }

    // ── the rebuild ──

    private static SubmissionSnapshot FirmSnapshot()
    {
        var withFirm = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal };
        return SubmissionSnapshot.Read(withFirm.Row).Snapshot!;
    }

    [Fact(DisplayName = "095d-2a-1: the read rebuilt from the stored rows (rounded as Dataverse stores them; no document read) evaluates with the firm exactly as the document read does")]
    public async Task TheRebuiltRead_EvaluatesAsTheDocumentReadDoes_WithoutReadingTheDocument()
    {
        BillingRuleCandidate[] rules =
        [
            Version("BILL-AI", RuleType.AiObservation), Version("BILL-TK", RuleType.ApprovedTimekeeper), Version("BILL-INC", RuleType.TimeIncrement),
        ];
        var intake = await PersistNoFirmIntakeAsync();

        var rebuilt = Rebuild(intake);
        rebuilt.IsRerunnable.Should().BeTrue();
        rebuilt.Read!.Lines[1].Date.Value.Should().BeNull("the stored flag restores the missing date");
        rebuilt.Read.Lines[0].Date.Value.Should().Be(new DateOnly(2026, 5, 20));
        rebuilt.Read.Lines[1].Rate.Repair.Should().Be(ExtractedValueRepair.RateFromTimekeeperSummary);
        rebuilt.Read.Lines[2].Hours.Value.Should().Be(0.125m, "review L1: the exact hours, not the column's 0.13");
        rebuilt.Read.Lines[3].UnitCost.Value.Should().Be(0.655m, "review L1: the exact unit cost, not the column's 0.66");
        rebuilt.Read.Header.FeesTotal.Value.Should().Be(1500.125m);
        rebuilt.Read.Header.TotalDue.Value.Should().Be(1474.625m, "review L4: from the stored number, not the 0.00 header text");
        rebuilt.Read.TimekeeperSummary.Select(r => r.Name.Value).Should().Equal("Jane Smith", SummaryOnlyName);

        // The document read, with the firm.
        var direct = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = rules, Ai = ReadingAi(RerunRead(RerunLines())), Lines = RerunLines() };
        direct.Coding.Observations = [BlockBillingQuote];
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
        JsonSerializer.Serialize(rerun.Resolution.Input).Should().Be(JsonSerializer.Serialize(direct.Resolution.Input), "the resolution sees the same totals, names, dates and numbers");

        // The persister's builders give the same stored copy (the invoice name aside: the firm name is not a stored fact and decides no rule).
        static string Stored(SubmissionEvaluationOutcome outcome)
        {
            var root = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId))!.AsObject();
            root.Remove("invoiceName");
            return root.ToJsonString();
        }

        Stored(actual).Should().Be(Stored(expected));
    }

    [Fact(DisplayName = "095d-2a-1 (L2): an invoice intake stored with no lines and coded is rebuilt coded, not refused")]
    public async Task AZeroLineCodedInvoice_IsRebuilt()
    {
        var intake = await PersistNoFirmIntakeAsync(lines: []);
        Provenance(intake.Findings).GetProperty(ExtractionProvenance.LineCountMember).GetInt32().Should().Be(0, "the setup");
        JsonNode.Parse(intake.Findings)!["codeSet"].Should().NotBeNull("the setup: intake coded the invoice");

        var rebuilt = Rebuild(intake);

        rebuilt.IsRerunnable.Should().BeTrue();
        rebuilt.Read!.Lines.Should().BeEmpty();
        rebuilt.Coding!.Coded.Should().NotBeNull("with no line, the invoice-level set says intake coded");
        rebuilt.Coding.Coded!.Lines.Should().BeEmpty();
        rebuilt.Coding.HoldReasonCode.Should().BeNull();
    }

    [Fact(DisplayName = "095d-2a-1 (L1): a number a reviewer corrected (the column no longer its exact value's rounding) is the column; an untouched one is the exact value")]
    public async Task ACorrectedNumber_IsTheColumn_AnUntouchedOneTheExactValue()
    {
        var intake = await PersistNoFirmIntakeAsync();
        var lines = intake.Lines.Select(Copy).ToList();
        lines[2]["sprk_quantity"] = 0.5m;
        lines[3]["sprk_rate"] = new Money(0.70m);

        var rebuilt = Rebuild(intake, lines: lines);

        rebuilt.Read!.Lines[2].Hours.Value.Should().Be(0.5m);
        rebuilt.Read.Lines[3].UnitCost.Value.Should().Be(0.70m);
        rebuilt.Read.Lines[3].Quantity.Value.Should().Be(100m, "must-not-fire: an untouched number keeps its exact value");
        rebuilt.Read.Lines[0].Hours.Value.Should().Be(1.5m);
    }

    [Fact(DisplayName = "095d-2a-1 (L3): a header field or line date a reviewer corrected or confirmed is its column; an unverified one is the value read")]
    public async Task ACorrectedOrConfirmedField_IsItsColumn()
    {
        var intake = await PersistNoFirmIntakeAsync();
        var invoice = Copy(intake.Invoice);
        invoice["sprk_invoicedate"] = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        invoice["sprk_totalamount"] = new Money(1400m);
        var verified = new HashSet<(Guid? LineId, string Field)>
        {
            (null, BillingFieldNames.InvoiceDate),
            (null, ResolutionFieldNames.TotalDue),
            (intake.Lines[1].Id, ResolutionFieldNames.Date),
        };

        var corrected = Rebuild(intake, invoice: invoice, verified: verified);
        var unverified = Rebuild(intake, invoice: invoice);

        corrected.Read!.Header.InvoiceDate.Value.Should().Be(new DateOnly(2026, 6, 15));
        corrected.Read.Header.TotalDue.Value.Should().Be(1400m);
        corrected.Read.Lines[1].Date.Value.Should().Be(RerunInvoiceDate, "a corrected or confirmed date overrides the stored no-date flag");
        corrected.Read.Header.BillingPeriodStart.Value.Should().Be(new DateOnly(2026, 5, 1), "must-not-fire: a field nobody verified");

        unverified.Read!.Header.InvoiceDate.Value.Should().Be(RerunInvoiceDate, "must-not-fire: the value read, whatever the column holds");
        unverified.Read.Header.TotalDue.Value.Should().Be(1474.625m);
        unverified.Read.Lines[1].Date.Value.Should().BeNull();
    }

    [Fact(DisplayName = "095d-2a-1 (V1): a number a reviewer corrected to its exact value's own rounding is the column, not the exact value")]
    public async Task ACorrectionToTheRoundedValue_Wins()
    {
        // Line 2 was read as 0.125 hours; the column holds 0.13. A reviewer corrects the hours to 0.13: the column still equals the rounding,
        // so only the correction row tells the reviewer's 0.13 from intake's 0.125.
        var intake = await PersistNoFirmIntakeAsync();
        intake.Lines[2].GetAttributeValue<decimal?>("sprk_quantity").Should().Be(0.13m, "the setup: the column is the exact value's rounding");
        var verified = new HashSet<(Guid? LineId, string Field)> { (intake.Lines[2].Id, ResolutionFieldNames.Hours) };

        var corrected = Rebuild(intake, verified: verified);
        var unverified = Rebuild(intake);

        corrected.Read!.Lines[2].Hours.Value.Should().Be(0.13m, "the reviewer's value");
        corrected.Read.Lines[3].UnitCost.Value.Should().Be(0.655m, "must-not-fire: a number nobody verified keeps its exact value");
        unverified.Read!.Lines[2].Hours.Value.Should().Be(0.125m, "must-not-fire: unverified, the exact value read");
    }

    /// <summary>True when <paramref name="finding"/> reads the extracted <paramref name="field"/> below the reliability threshold.</summary>
    private static bool LowConfidenceRead(BillingFinding finding, string field) =>
        finding.ValuesRead.Any(v => v.ExtractedField?.Field == field && v.BelowReliabilityThreshold);
    [Fact(DisplayName = "095d-2a-1 (V2): a low-confidence field a reviewer corrected or confirmed is judged at 1.0 on the re-run (no low-confidence finding); an unverified one still raises it")]
    public async Task AVerifiedLowConfidenceField_RaisesNoLowConfidenceFinding_OnTheRerun()
    {
        // The currency read at 0.5 (below the threshold, so money is "unclear") and line 2's hours read at 0.5 (its TimeIncrement finding
        // reads them). Both are what intake stored; the rebuilt read keeps the analyzer's 0.5.
        var lines = RerunLines().Select((line, i) => i == 2 ? line with { Hours = InvoiceSubmissionProcessorTests.F<decimal?>(0.125m, 0.5m) } : line).ToList();
        var intake = await PersistNoFirmIntakeAsync(lines: lines, currencyConfidence: 0.5m);
        var confirmed = new HashSet<(Guid? LineId, string Field)>
        {
            (null, BillingFieldNames.Currency),
            (intake.Lines[2].Id, ResolutionFieldNames.Hours),
        };

        async Task<(ProcessorHarness Harness, SubmissionEvaluationOutcome Outcome)> RerunAsync(IReadOnlySet<(Guid? LineId, string Field)> verified)
        {
            var rebuilt = Rebuild(intake, verified: verified);
            rebuilt.IsRerunnable.Should().BeTrue("the setup");
            rebuilt.Read!.Header.Currency.Confidence.Should().Be(0.5m, "the read keeps the analyzer's confidence; the evaluation judges a verified field");
            var harness = new ProcessorHarness
            {
                Channel = InvoiceSubmissionChannel.Internal,
                Rules = [Version("BILL-RATE", RuleType.ApprovedRate), Version("BILL-INC", RuleType.TimeIncrement)],
                Ai = new Mock<IInvoiceAi>(MockBehavior.Strict).Object,
            };
            var coding = new RerunLineCoding(harness.Coding, (_, _) => Task.FromResult<ResolvedCodeSet?>(null), rebuilt.Coding!, new CapturingLogger());
            var outcome = await harness.Build().EvaluateFromReadAsync(FirmSnapshot(), rebuilt.Read, finalDelivery: false, coding, rebuilt.Verified);
            return (harness, outcome);
        }


        // Must-fire: confirmed, both are judged at 1.0, exactly as InvoiceCorrectionReevaluation judges them.
        var (verifiedRun, verifiedOutcome) = await RerunAsync(confirmed);
        verifiedRun.Evaluated!.HeaderConfidence[BillingFieldNames.Currency].Should().Be(1m);
        verifiedRun.Evaluated.Lines[2].FieldConfidence[ResolutionFieldNames.Hours].Should().Be(1m);
        verifiedOutcome.Evaluation!.Skips.Should().BeEmpty("a confirmed currency is judged, not unclear");
        verifiedOutcome.Evaluation.Findings.Should().NotContain(f => f.ReasonCode == BillingEvaluationReasons.CurrencyUnreliable);
        verifiedOutcome.Evaluation.Findings.Should().Contain(f => f.Subject.LineIndex == 2 && f.RuleType == RuleType.TimeIncrement, "the setup: the finding still fires")
            .Which.ValuesRead.Should().NotContain(v => v.BelowReliabilityThreshold, "the confirmed hours are not low-confidence");
        verifiedOutcome.Evaluation.Findings.Should().NotContain(f => LowConfidenceRead(f, BillingFieldNames.Currency) || LowConfidenceRead(f, ResolutionFieldNames.Hours));
        verifiedRun.Evaluated.Lines[0].FieldConfidence[ResolutionFieldNames.Hours].Should().Be(0.99m, "must-not-fire: another line's hours are the analyzer's");

        // Must-not-fire: unverified, the same stored fields raise the low-confidence findings.
        var (unverifiedRun, unverifiedOutcome) = await RerunAsync(NoneVerified);
        unverifiedRun.Evaluated!.HeaderConfidence[BillingFieldNames.Currency].Should().Be(0.5m);
        unverifiedOutcome.Evaluation!.Findings.Should().Contain(f => f.ReasonCode == BillingEvaluationReasons.CurrencyUnreliable && LowConfidenceRead(f, BillingFieldNames.Currency));
        unverifiedOutcome.Evaluation.Findings.Should().Contain(f => f.Subject.LineIndex == 2 && f.RuleType == RuleType.TimeIncrement && LowConfidenceRead(f, ResolutionFieldNames.Hours));
    }

    // ── fail closed: a submission stored without the facts ──

    /// <summary>A stored copy without the re-run facts (as intake wrote it before task 095d-2a-1, or with a firm): <paramref name="only"/> one, or all.</summary>
    private static (string Findings, List<Entity> Lines) AsStoredBefore(string findings, List<Entity> lines, string? only = null)
    {
        var root = JsonNode.Parse(findings)!.AsObject();
        var provenance = root[ExtractionProvenance.Member]!.AsObject();
        foreach (var member in new[] { ExtractionProvenance.SectionTotalsMember, ExtractionProvenance.TimekeeperSummaryMember, ExtractionProvenance.ObservationsMember, ExtractionProvenance.LineCountMember })
        {
            if (only is null || only == member)
            {
                provenance.Remove(member);
            }
        }

        var stripped = lines.Select(l =>
        {
            var copy = Copy(l);
            var extraction = JsonNode.Parse(l.GetAttributeValue<string>("sprk_extractionconfidence"))!.AsObject();
            if (only is null || only == ExtractionProvenance.LineDateMissingMember)
            {
                extraction.Remove(ExtractionProvenance.LineDateMissingMember);
            }

            if (only is null || only == ExtractionProvenance.ExactMember)
            {
                foreach (var name in ExtractionProvenance.ExactLineFieldNames)
                {
                    extraction["fields"]![name]!.AsObject().Remove(ExtractionProvenance.ExactMember);
                }
            }

            copy["sprk_extractionconfidence"] = extraction.ToJsonString();
            return copy;
        }).ToList();
        return (root.ToJsonString(), stripped);
    }

    [Theory(DisplayName = "095d-2a-1 SEEDED: a submission stored without the re-run facts (any one of them) is not re-runnable: a typed refusal, nothing rebuilt, logged Permanent=true")]
    [InlineData(null)]
    [InlineData(ExtractionProvenance.SectionTotalsMember)]
    [InlineData(ExtractionProvenance.TimekeeperSummaryMember)]
    [InlineData(ExtractionProvenance.ObservationsMember)]
    [InlineData(ExtractionProvenance.LineCountMember)]
    [InlineData(ExtractionProvenance.LineDateMissingMember)]
    [InlineData(ExtractionProvenance.ExactMember)]
    public async Task ASubmissionStoredWithoutTheFacts_RefusesTheRerun_FailClosed(string? missing)
    {
        var intake = await PersistNoFirmIntakeAsync();
        var transactions = intake.Store.Transactions.Count;
        var (older, olderLines) = AsStoredBefore(intake.Findings, intake.Lines, missing);
        var logger = new CapturingLogger();

        var result = Rebuild(intake, findings: older, lines: olderLines, logger: logger);

        result.IsRerunnable.Should().BeFalse();
        result.RefusalReason.Should().Be(StoredSubmissionRead.FactsMissing);
        result.Read.Should().BeNull("no fact is guessed");
        result.Coding.Should().BeNull();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message
            .Should().Contain("Permanent=True").And.Contain(StoredSubmissionRead.FactsMissing);
        intake.Store.Transactions.Should().HaveCount(transactions, "the rebuild writes nothing, so the hold stays");

        // Must-not-fire: the same submission as stored now is re-runnable, and logs no refusal.
        var control = new CapturingLogger();
        Rebuild(intake, logger: control).IsRerunnable.Should().BeTrue();
        control.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Theory(DisplayName = "095d-2a-1: stored rows that do not agree with the stored facts are not re-runnable (stored_rows_inconsistent, never facts_missing)")]
    [InlineData("trailing row missing")]
    [InlineData("first row missing")]
    [InlineData("quote without its observation")]
    [InlineData("header entry missing")]
    [InlineData("line fields missing")]
    [InlineData("line extraction JSON not written (over 100,000 characters)")]
    public async Task InconsistentStoredRows_AreNotRerunnable(string inconsistency)
    {
        var intake = await PersistNoFirmIntakeAsync();
        var findings = JsonNode.Parse(intake.Findings)!.AsObject();
        var lines = intake.Lines.Select(Copy).ToList();
        switch (inconsistency)
        {
            case "trailing row missing":
                // Its quote goes too, so only the stored line count can tell (the rows left are contiguous and every quote has its line).
                lines.RemoveAt(lines.Count - 1);
                var quotes = findings[ExtractionProvenance.Member]![ExtractionProvenance.ObservationsMember]!.AsArray();
                foreach (var quote in quotes.Where(q => q!["line"]!.GetValue<int>() == lines.Count).ToList())
                {
                    quotes.Remove(quote);
                }

                break;
            case "first row missing":
                lines.RemoveAt(0);
                break;
            case "quote without its observation":
                findings[ExtractionProvenance.Member]![ExtractionProvenance.ObservationsMember]!.AsArray()
                    .Add(new JsonObject { ["line"] = 0, ["type"] = "VagueDescription", ["quotedText"] = "x" });
                break;
            case "header entry missing":
                findings[ExtractionProvenance.Member]![ExtractionProvenance.HeaderMember]!.AsObject().Remove(BillingFieldNames.Currency);
                break;
            case "line fields missing":
                var extraction = JsonNode.Parse(lines[0].GetAttributeValue<string>("sprk_extractionconfidence"))!.AsObject();
                extraction.Remove("fields");
                lines[0]["sprk_extractionconfidence"] = extraction.ToJsonString();
                break;
            default:
                // The persister leaves the column unset when the JSON is over 100,000 characters (BuildLines).
                lines[0].Attributes.Remove("sprk_extractionconfidence");
                break;
        }

        var result = Rebuild(intake, findings: findings.ToJsonString(), lines: lines);

        result.RefusalReason.Should().Be(StoredSubmissionRead.StoredRowsInconsistent, inconsistency);
        Rebuild(intake).IsRerunnable.Should().BeTrue("must-not-fire: the rows as stored");
    }
}
