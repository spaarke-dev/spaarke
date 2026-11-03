using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Resolution;

/// <summary>
/// <see cref="InvoiceResolutionService"/> (ebilling-r1 task 046, FR-23, D-47): Processing Pipeline step 2 end to end over a faked
/// Dataverse and a faked embedding service.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): this is the contract task 049 builds on. The guarantees pinned here are the ones a later caller cannot see
/// from the outside: the reference-data reads are bounded however long the invoice is, an unmatched timekeeper is a finding input and
/// never another person, only the combined confidence is reported per field, and a Dataverse outage is an error rather than a
/// resolution made from half the roster. Dataverse is mocked at <see cref="IGenericEntityService"/> (the narrow interface the service
/// takes) and every query it receives is recorded, so the tests can assert on what was asked as well as on what came back.
/// </remarks>
public sealed class InvoiceResolutionServiceTests
{
    private static readonly Guid Firm = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherFirm = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MatterId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid OtherMatterId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly DateOnly Day = new(2026, 6, 15);
    private const int Partner = 1;
    private const int Published = 659490000;
    private const int Draft = 1;

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IEbillingSettingsReader> _settings = new();
    private readonly Mock<IInvoiceAi> _ai = new();
    private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
    private readonly List<string> _embedded = [];
    private readonly List<Call> _calls = [];
    private readonly CapturingLogger<InvoiceResolutionService> _log = new();
    private List<List<Entity>> _rosterPages = [[]];
    private List<List<Entity>> _ratePages = [[]];

    public InvoiceResolutionServiceTests()
    {
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());

        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((query, _) =>
            {
                _calls.Add(Call.Of(query));
                var pages = query.EntityName switch
                {
                    "sprk_timekeeper" => _rosterPages,
                    "sprk_ratecardrate" => _ratePages,
                    _ => throw new InvalidOperationException($"Unexpected query on {query.EntityName}"),
                };
                var index = Math.Max(0, query.PageInfo.PageNumber - 1);
                var page = new EntityCollection(index < pages.Count ? pages[index].ToList() : []);
                page.MoreRecords = index < pages.Count - 1;
                page.PagingCookie = page.MoreRecords ? $"cookie{index + 1}" : null;
                return Task.FromResult(page);
            });

        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, int?, CancellationToken>((text, _, _, _) =>
            {
                _embedded.Add(text);
                return Task.FromResult(new ReadOnlyMemory<float>(_vectors.TryGetValue(text, out var v) ? v : [0f, 0f, 0f, 1f]));
            });
    }

    private InvoiceResolutionService CreateService() => new(
        _entities.Object,
        _settings.Object,
        new TimekeeperMatcher(_ai.Object, Mock.Of<ILogger<TimekeeperMatcher>>()),
        _log);

    private void GivenSettings(EbillingSettings settings) =>
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

    private void GivenRoster(params Entity[] rows) => _rosterPages = [rows.ToList()];

    private void GivenRates(params Entity[] rows) => _ratePages = [rows.ToList()];

    // ── Fixture rows ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Entity Person(
        string name,
        Guid? id = null,
        Guid? firm = null,
        int status = 1,
        int state = 0,
        int? classification = Partner,
        DateTime? from = null,
        DateTime? to = null)
    {
        var row = new Entity("sprk_timekeeper", id ?? Guid.NewGuid())
        {
            ["sprk_name"] = name,
            ["sprk_firm"] = new EntityReference("sprk_organization", firm ?? Firm),
            ["statecode"] = new OptionSetValue(state),
            ["statuscode"] = new OptionSetValue(status),
        };
        if (classification is { } c) row["sprk_classification"] = new OptionSetValue(c);
        if (from is { } f) row["sprk_effectivefrom"] = f;
        if (to is { } t) row["sprk_effectiveto"] = t;
        return row;
    }

    private static AliasedValue Card(string column, object value) => new("sprk_ratecard", column, value);

    private static Entity Rate(
        decimal approved,
        Guid? timekeeper = null,
        int? classification = null,
        Guid? firm = null,
        int cardStatus = Published,
        int cardState = 0,
        string? scopeColumn = null,
        Guid? scopeId = null,
        Guid? cardId = null,
        int rateState = 0,
        DateTime? rateFrom = null,
        DateTime? rateTo = null)
    {
        var row = new Entity("sprk_ratecardrate", Guid.NewGuid())
        {
            ["sprk_ratecard"] = new EntityReference("sprk_ratecard", cardId ?? Guid.NewGuid()),
            ["sprk_approvedrate"] = new Money(approved),
            ["sprk_rackrate"] = new Money(approved + 100m),
            ["statecode"] = new OptionSetValue(rateState),
            ["card.sprk_firm"] = Card("sprk_firm", new EntityReference("sprk_organization", firm ?? Firm)),
            ["card.statuscode"] = Card("statuscode", new OptionSetValue(cardStatus)),
            ["card.statecode"] = Card("statecode", new OptionSetValue(cardState)),
        };
        if (timekeeper is { } tk) row["sprk_timekeeper"] = new EntityReference("sprk_timekeeper", tk);
        if (classification is { } c) row["sprk_classification"] = new OptionSetValue(c);
        if (rateFrom is { } from) row["sprk_effectivefrom"] = from;
        if (rateTo is { } to) row["sprk_effectiveto"] = to;
        if (scopeColumn is not null) row[$"card.{scopeColumn}"] = Card(scopeColumn, new EntityReference("sprk_matter", scopeId!.Value));
        return row;
    }

    private static InvoiceResolutionLine Fee(
        string? name, DateOnly? date, decimal? hours, decimal? rate, decimal? amount, decimal analyzer = 0.95m, decimal? self = null) => new()
    {
        LineType = InvoiceLineType.Fee,
        Date = new ResolutionField<DateOnly?>(date, analyzer),
        TimekeeperAsBilled = new ResolutionField<string?>(name, analyzer, self),
        Hours = new ResolutionField<decimal?>(hours, analyzer, self),
        Rate = new ResolutionField<decimal?>(rate, analyzer, self),
        Amount = new ResolutionField<decimal?>(amount, analyzer, self),
    };

    private static InvoiceResolutionLine Expense(decimal? quantity, decimal? unitCost, decimal? amount, decimal analyzer = 0.95m) => new()
    {
        LineType = InvoiceLineType.Expense,
        Date = new ResolutionField<DateOnly?>(Day, analyzer),
        Quantity = new ResolutionField<decimal?>(quantity, analyzer),
        UnitCost = new ResolutionField<decimal?>(unitCost, analyzer),
        Amount = new ResolutionField<decimal?>(amount, analyzer),
    };

    private static InvoiceResolutionInput Invoice(
        IReadOnlyList<InvoiceResolutionLine> lines,
        decimal? fees = null,
        decimal? expenses = null,
        decimal? totalDue = null,
        CoreRecordReference? core = null,
        IReadOnlyList<string>? summary = null,
        InvoiceResolutionHeader? header = null,
        decimal analyzer = 0.95m,
        decimal? adjustments = null) => new()
    {
        FirmId = Firm,
        CoreRecord = core,
        Lines = lines,
        TimekeeperSummaryNames = summary ?? [],
        Header = header ?? new InvoiceResolutionHeader
        {
            FeesTotal = new ResolutionField<decimal?>(fees, analyzer),
            ExpensesTotal = new ResolutionField<decimal?>(expenses, analyzer),
            AdjustmentsTotal = new ResolutionField<decimal?>(adjustments, analyzer),
            TotalDue = new ResolutionField<decimal?>(totalDue, analyzer),
        },
    };

    /// <summary>A clean one-line invoice: Jane Doe, 2 h at 500, 1000 due, every header total extracted.</summary>
    private InvoiceResolutionInput CleanInvoice(CoreRecordReference? core = null)
    {
        GivenRoster(Person("Jane Doe"));
        GivenRates(Rate(500m, classification: Partner));
        return Invoice(
            [Fee("Jane Doe", Day, 2m, 500m, 1000m)], fees: 1000m, expenses: 0m, adjustments: 0m, totalDue: 1000m, core: core);
    }

    // ── The whole pipeline step ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACleanInvoice_ResolvesEveryFieldAboveTheReliabilityThreshold()
    {
        var result = await CreateService().ResolveAsync(CleanInvoice());

        var line = result.Lines.Should().ContainSingle().Subject;
        line.Timekeeper!.IsMatched.Should().BeTrue();
        line.Rate.Status.Should().Be(RateResolutionStatus.Found);
        line.Rate.ApprovedRate.Should().Be(500m);
        result.ArithmeticChecks.Should().OnlyContain(c => c.Passed);
        result.ApprovedTimekeeperFindingInputs.Should().BeEmpty();

        var threshold = new EbillingSettings().ReliabilityThreshold;
        line.Fields.Keys.Should().BeEquivalentTo(
            ResolutionFieldNames.Date, ResolutionFieldNames.TimekeeperAsBilled, ResolutionFieldNames.Hours,
            ResolutionFieldNames.Rate, ResolutionFieldNames.Amount);
        line.Fields.Values.Should().OnlyContain(f => f.Combined >= threshold);
        result.HeaderFields.Values.Should().OnlyContain(f => f.Combined >= threshold);
        line.Fields[ResolutionFieldNames.TimekeeperAsBilled].Reference.Should().Be(ReferenceOutcome.Matched);
        line.Fields[ResolutionFieldNames.Rate].Reference.Should().Be(ReferenceOutcome.Matched);
        line.Fields[ResolutionFieldNames.Hours].Arithmetic.Should().Be(ArithmeticOutcome.Passed);
        line.Fields[ResolutionFieldNames.Date].Arithmetic.Should().Be(ArithmeticOutcome.NotApplicable);
    }

    [Fact]
    public async Task AFieldThatWasNotExtractedAtAll_ScoresZero_FailClosed_RatherThanBeingLeftOut()
    {
        GivenRoster(Person("Jane Doe"));

        var header = new InvoiceResolutionHeader { FeesTotal = new ResolutionField<decimal?>(1000m, 0.95m) };

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 2m, 500m, 1000m)], header: header));

        var adjustments = result.HeaderFields[ResolutionFieldNames.AdjustmentsTotal];
        adjustments.Combined.Should().Be(0m);
        adjustments.AnalyzerSignal.Should().Be(0m);
        adjustments.Arithmetic.Should().Be(ArithmeticOutcome.NotApplicable);
        result.HeaderFields.Keys.Should().Contain(
            [ResolutionFieldNames.FeesTotal, ResolutionFieldNames.ExpensesTotal, ResolutionFieldNames.AdjustmentsTotal, ResolutionFieldNames.TotalDue]);
    }

    [Fact]
    public async Task AnUnmatchedTimekeeper_IsAFindingInput_NeverAnotherPerson_AndItsFieldsStayBelowTheThreshold()
    {
        GivenRoster(Person("Jane Doe"));
        var input = Invoice(
            [Fee("Nobody Here", Day, 2m, 500m, 1000m), Fee("Jane Doe", Day, 1m, 500m, 500m)],
            fees: 1500m, totalDue: 1500m);

        var result = await CreateService().ResolveAsync(input);

        var unmatched = result.Lines[0];
        unmatched.Timekeeper!.IsMatched.Should().BeFalse();
        unmatched.Timekeeper.TimekeeperId.Should().BeNull();
        unmatched.Rate.Status.Should().Be(RateResolutionStatus.NotFound);
        unmatched.Rate.Reason.Should().Be(RateNotFoundReason.TimekeeperUnmatched);

        var finding = result.ApprovedTimekeeperFindingInputs.Should().ContainSingle().Subject;
        finding.LineIndex.Should().Be(0);
        finding.BilledName.Should().Be("Nobody Here");
        finding.LineDate.Should().Be(Day);
        finding.Reason.Should().Be(TimekeeperUnmatchedReason.NotOnRoster);

        var name = unmatched.Fields[ResolutionFieldNames.TimekeeperAsBilled];
        name.Reference.Should().Be(ReferenceOutcome.Missing);
        name.CapsInForce.Should().HaveFlag(ConfidenceCaps.ReferenceMissing);
        name.Combined.Should().BeLessThanOrEqualTo(new EbillingSettings().ReferenceMissingCap).And.BeLessThan(new EbillingSettings().ReliabilityThreshold);
        unmatched.Fields[ResolutionFieldNames.Rate].Combined.Should().BeLessThan(new EbillingSettings().ReliabilityThreshold);
        result.Lines[1].Timekeeper!.IsMatched.Should().BeTrue();
    }

    [Fact]
    public async Task AFailedArithmeticCheck_CapsEveryFieldItInvolved_AndOnlyThose()
    {
        // 2 h x 500 = 1000, but the line says 900.
        GivenRoster(Person("Jane Doe"));
        GivenRates(Rate(500m, classification: Partner));
        var input = Invoice([Fee("Jane Doe", Day, 2m, 500m, 900m)], fees: 900m, totalDue: 900m);

        var result = await CreateService().ResolveAsync(input);

        var line = result.Lines.Single();
        var cap = new EbillingSettings().ArithmeticFailCap;
        foreach (var field in new[] { ResolutionFieldNames.Hours, ResolutionFieldNames.Rate, ResolutionFieldNames.Amount })
        {
            line.Fields[field].Arithmetic.Should().Be(ArithmeticOutcome.Failed, field);
            line.Fields[field].CapsInForce.Should().HaveFlag(ConfidenceCaps.ArithmeticFail);
            line.Fields[field].Combined.Should().BeLessThanOrEqualTo(cap, field);
        }

        line.Fields[ResolutionFieldNames.Date].Combined.Should().BeGreaterThan(cap, "the date is in no check");
        line.Fields[ResolutionFieldNames.TimekeeperAsBilled].CapsInForce.Should().Be(ConfidenceCaps.None);
        result.ArithmeticChecks.Should().Contain(c => c.Kind == ArithmeticCheckKind.LineProduct && !c.Passed);
    }

    [Fact]
    public async Task ASelfReportBelowTheAnalyzer_LowersAField_AndOneAboveItNeverRaisesIt()
    {
        var baseline = await CreateService().ResolveAsync(CleanInvoice());
        var doubtful = await CreateService().ResolveAsync(WithSelfReport(0.2m));
        var overconfident = await CreateService().ResolveAsync(WithSelfReport(1.0m));

        var key = ResolutionFieldNames.Hours;
        doubtful.Lines[0].Fields[key].Combined.Should().BeLessThan(baseline.Lines[0].Fields[key].Combined);
        overconfident.Lines[0].Fields[key].SelfReportSignal.Should().Be(0.95m);

        InvoiceResolutionInput WithSelfReport(decimal self)
        {
            var clean = CleanInvoice();
            return clean with { Lines = [Fee("Jane Doe", Day, 2m, 500m, 1000m, self: self)] };
        }
    }

    [Fact]
    public async Task ChangingTheSettings_ChangesTheResult()
    {
        var input = CleanInvoice();
        var seeded = await CreateService().ResolveAsync(input);

        GivenSettings(new EbillingSettings { ArithmeticFailCap = 0.30m, WeightAnalyzer = 0.10m });
        var badInput = input with { Lines = [Fee("Jane Doe", Day, 2m, 500m, 900m)] };
        var changed = await CreateService().ResolveAsync(badInput);

        changed.Lines[0].Fields[ResolutionFieldNames.Hours].Combined.Should().BeLessThanOrEqualTo(0.30m);

        // A field that fails nothing still moves with the weights: (0.10 x 0.95 + 0.15) / 0.25 against (0.50 x 0.95 + 0.15) / 0.65.
        var name = ResolutionFieldNames.TimekeeperAsBilled;
        seeded.Lines[0].Fields[name].Combined.Should().Be(0.625m / 0.65m);
        changed.Lines[0].Fields[name].Combined.Should().Be(0.245m / 0.25m);
    }

    [Fact]
    public async Task TheReliabilityThresholdSetting_DecidesTheAiAssistedNameMatch()
    {
        GivenRoster(Person("Jonathan Smith"));
        _vectors["jon smith"] = [1f, 0f, 0f, 0f];
        _vectors["jonathan smith"] = [0.95f, MathF.Sqrt(1 - 0.95f * 0.95f), 0f, 0f];
        var input = Invoice([Fee("Jon Smith", Day, 1m, 100m, 100m)]);

        GivenSettings(new EbillingSettings { ReliabilityThreshold = 0.90m });
        var atDefault = await CreateService().ResolveAsync(input);

        GivenSettings(new EbillingSettings { ReliabilityThreshold = 0.99m });
        var stricter = await CreateService().ResolveAsync(input);

        atDefault.Lines[0].Timekeeper!.Stage.Should().Be(TimekeeperMatchStage.AiAssisted);
        atDefault.Lines[0].Fields[ResolutionFieldNames.TimekeeperAsBilled].ReferenceStrength.Should().BeApproximately(0.95m, 0.0001m);
        stricter.Lines[0].Timekeeper!.IsMatched.Should().BeFalse();
        stricter.Lines[0].Timekeeper!.Reason.Should().Be(TimekeeperUnmatchedReason.AiBelowThreshold);
        stricter.ApprovedTimekeeperFindingInputs.Should().ContainSingle();
    }

    [Fact]
    public async Task AnAiAssistedMatch_IsWeakerEvidenceThanAnExactOne_ForTheNameAndForTheRate()
    {
        GivenRoster(Person("Jonathan Smith"), Person("Jane Doe"));
        GivenRates(Rate(100m, classification: Partner));
        _vectors["jon smith"] = [1f, 0f, 0f, 0f];
        _vectors["jonathan smith"] = [0.92f, MathF.Sqrt(1 - 0.92f * 0.92f), 0f, 0f];

        var result = await CreateService().ResolveAsync(Invoice(
            [Fee("Jon Smith", Day, 1m, 100m, 100m), Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        var ai = result.Lines[0].Fields;
        var exact = result.Lines[1].Fields;
        ai[ResolutionFieldNames.TimekeeperAsBilled].Combined.Should().BeLessThan(exact[ResolutionFieldNames.TimekeeperAsBilled].Combined);
        ai[ResolutionFieldNames.Rate].Combined.Should().BeLessThan(exact[ResolutionFieldNames.Rate].Combined);
        ai[ResolutionFieldNames.Rate].ReferenceStrength.Should().BeApproximately(0.92m, 0.0001m);
    }

    [Fact]
    public async Task ExpenseLines_HaveNoTimekeeperAndNoRate_AndGetTheirOwnFields()
    {
        var input = Invoice([Expense(3m, 12.50m, 37.50m)], expenses: 37.50m, totalDue: 37.50m);

        var result = await CreateService().ResolveAsync(input);

        var line = result.Lines.Single();
        line.Timekeeper.Should().BeNull();
        line.Rate.Status.Should().Be(RateResolutionStatus.NotApplicable);
        line.Fields.Keys.Should().BeEquivalentTo(
            ResolutionFieldNames.Date, ResolutionFieldNames.Quantity, ResolutionFieldNames.UnitCost, ResolutionFieldNames.Amount);
        line.Fields[ResolutionFieldNames.Quantity].Arithmetic.Should().Be(ArithmeticOutcome.Passed);
        result.ApprovedTimekeeperFindingInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryInputLineHasOneResult_InInputOrder_WithItsOwnIndex()
    {
        GivenRoster(Person("Jane Doe"), Person("John Roe"));
        var input = Invoice(
        [
            Fee("John Roe", Day, 1m, 100m, 100m),
            Expense(1m, 10m, 10m),
            Fee("Nobody Here", Day, 1m, 100m, 100m),
            Fee("Jane Doe", Day, 1m, 100m, 100m),
        ]);

        var result = await CreateService().ResolveAsync(input);

        result.Lines.Select(l => l.LineIndex).Should().Equal(0, 1, 2, 3);
        result.Lines.Select(l => l.LineType).Should().Equal(
            InvoiceLineType.Fee, InvoiceLineType.Expense, InvoiceLineType.Fee, InvoiceLineType.Fee);
        result.ApprovedTimekeeperFindingInputs.Select(f => f.LineIndex).Should().Equal(2);
    }

    [Fact]
    public async Task OtherFields_GetACombinedConfidence_FromTheAnalyzerAndSelfReportOnly()
    {
        var header = new InvoiceResolutionHeader
        {
            OtherFields = new Dictionary<string, FieldReading> { ["InvoiceNumber"] = new(0.8m, 0.7m), ["Currency"] = new(null) },
        };
        var line = Fee("Jane Doe", Day, 2m, 500m, 1000m) with
        {
            OtherFields = new Dictionary<string, FieldReading> { ["Narrative"] = new(0.6m) },
        };
        GivenRoster(Person("Jane Doe"));
        GivenRates(Rate(500m, classification: Partner));

        var result = await CreateService().ResolveAsync(Invoice([line], header: header));

        // (0.50 x 0.80 + 0.10 x 0.70) / (0.50 + 0.10)
        result.HeaderFields["InvoiceNumber"].Combined.Should().Be(0.47m / 0.60m);
        result.HeaderFields["Currency"].Combined.Should().Be(0m);
        result.Lines[0].Fields["Narrative"].Combined.Should().Be(0.6m);
        result.HeaderFields["InvoiceNumber"].Arithmetic.Should().Be(ArithmeticOutcome.NotApplicable);
        result.HeaderFields["InvoiceNumber"].Reference.Should().Be(ReferenceOutcome.NotApplicable);
    }

    [Fact]
    public async Task ANamedField_WinsOverAnOtherFieldsEntryOfTheSameName()
    {
        var line = Fee("Jane Doe", Day, 2m, 500m, 1000m) with
        {
            OtherFields = new Dictionary<string, FieldReading> { [ResolutionFieldNames.Hours] = new(0.01m) },
        };
        GivenRoster(Person("Jane Doe"));

        var result = await CreateService().ResolveAsync(Invoice([line]));

        result.Lines[0].Fields[ResolutionFieldNames.Hours].AnalyzerSignal.Should().Be(0.95m);
    }

    [Fact]
    public async Task Resolve_IsDeterministic()
    {
        var input = CleanInvoice(new CoreRecordReference(CoreRecordKind.Matter, MatterId));

        var first = await CreateService().ResolveAsync(input);
        var second = await CreateService().ResolveAsync(input);

        second.Should().BeEquivalentTo(first);
    }

    // ── Who may be matched ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheRosterIsReadForTheInvoicesFirmOnly_AcrossEveryStatus()
    {
        await CreateService().ResolveAsync(CleanInvoice());

        var call = _calls.Single(c => c.Entity == "sprk_timekeeper");
        call.Conditions.Should().Contain(c => c.Attribute == "sprk_firm" && c.Operator == ConditionOperator.Equal && c.Values.SequenceEqual(new object[] { Firm }));
        call.Conditions.Should().NotContain(c => c.Attribute == "statuscode" || c.Attribute == "statecode",
            "an unapproved person must be told apart from an unknown one");
        call.PageSize.Should().Be(5000);
    }

    [Fact]
    public async Task AnotherFirmsRosterRow_ReturnedAnyway_IsStillNeverMatched()
    {
        GivenRoster(Person("Jane Doe", firm: OtherFirm));

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        result.Lines[0].Timekeeper!.IsMatched.Should().BeFalse();
        result.ApprovedTimekeeperFindingInputs.Should().ContainSingle();
    }

    [Theory]
    [InlineData(659490000, 0)] // Pending
    [InlineData(2, 1)]         // Inactive
    public async Task ATimekeeperWhoIsNotApproved_IsAFindingInput_WithTheReasonThatSaysSo(int status, int state)
    {
        GivenRoster(Person("Jane Doe", status: status, state: state));

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        result.ApprovedTimekeeperFindingInputs.Should().ContainSingle()
            .Which.Reason.Should().Be(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
    }

    [Fact]
    public async Task EffectiveDates_AreReadAsDays_AndAreInclusive()
    {
        GivenRoster(Person("Jane Doe", from: Day.ToDateTime(TimeOnly.MinValue), to: Day.ToDateTime(TimeOnly.MinValue)));

        var result = await CreateService().ResolveAsync(Invoice(
            [Fee("Jane Doe", Day.AddDays(-1), 1m, 100m, 100m), Fee("Jane Doe", Day, 1m, 100m, 100m), Fee("Jane Doe", Day.AddDays(1), 1m, 100m, 100m)]));

        result.Lines.Select(l => l.Timekeeper!.IsMatched).Should().Equal(false, true, false);
    }

    [Fact]
    public async Task InitialsOnTheLine_AreExpandedOnlyThroughTheInvoicesOwnTimekeeperSummary()
    {
        GivenRoster(Person("David Lockhart"));
        var line = Fee("DL", Day, 1m, 100m, 100m);

        var withSummary = await CreateService().ResolveAsync(Invoice([line], summary: ["David Lockhart"]));
        var without = await CreateService().ResolveAsync(Invoice([line]));

        withSummary.Lines[0].Timekeeper!.IsMatched.Should().BeTrue();
        without.Lines[0].Timekeeper!.IsMatched.Should().BeFalse();
        without.ApprovedTimekeeperFindingInputs.Should().ContainSingle().Which.Reason.Should().Be(TimekeeperUnmatchedReason.NameIncomplete);
    }

    [Fact]
    public async Task WhenAiIsOff_TheAiAssistedNameStepDegradesToUnmatched_AndTheInvoiceStillResolves()
    {
        GivenRoster(Person("Jonathan Smith"), Person("Jane Doe"));
        _ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ai off"));

        var result = await CreateService().ResolveAsync(Invoice(
            [Fee("Jon Smith", Day, 1m, 100m, 100m), Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        result.Lines[0].Timekeeper!.Reason.Should().Be(TimekeeperUnmatchedReason.AiUnavailable);
        result.Lines[1].Timekeeper!.IsMatched.Should().BeTrue();
        result.ApprovedTimekeeperFindingInputs.Should().ContainSingle().Which.LineIndex.Should().Be(0);
    }

    // ── Rates ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheRateQuery_AsksForThePublishedCardsOfTheFirm_InForceAcrossTheLineDates_FirmWideOrOfTheCoreRecord()
    {
        GivenRoster(Person("Jane Doe"));
        var input = Invoice(
            [Fee("Jane Doe", Day, 1m, 100m, 100m), Fee("Jane Doe", Day.AddDays(20), 1m, 100m, 100m), Fee("Jane Doe", Day.AddDays(-5), 1m, 100m, 100m)],
            core: new CoreRecordReference(CoreRecordKind.Matter, MatterId));

        await CreateService().ResolveAsync(input);

        var call = _calls.Single(c => c.Entity == "sprk_ratecardrate");
        call.LinkEntity.Should().Be("sprk_ratecard");
        var link = call.LinkConditions;
        link.Should().Contain(c => c.Attribute == "sprk_firm" && c.Values.SequenceEqual(new object[] { Firm }));
        link.Should().Contain(c => c.Attribute == "statuscode" && c.Values.SequenceEqual(new object[] { Published }));
        link.Should().Contain(c => c.Attribute == "sprk_matter" && c.Operator == ConditionOperator.Equal && c.Values.SequenceEqual(new object[] { MatterId }));
        link.Where(c => c.Operator == ConditionOperator.Null).Select(c => c.Attribute).Should()
            .Contain(["sprk_matter", "sprk_project", "sprk_workassignment"], "a firm-wide card has no scope");
        link.Should().NotContain(c => c.Attribute == "sprk_project" && c.Operator == ConditionOperator.Equal);

        // Date overlap on the card and on the rate: from <= latest line date, to >= earliest line date.
        var latest = Day.AddDays(20).ToDateTime(TimeOnly.MinValue);
        var earliest = Day.AddDays(-5).ToDateTime(TimeOnly.MinValue);
        foreach (var conditions in new[] { link, call.Conditions })
        {
            conditions.Should().Contain(c => c.Attribute == "sprk_effectivefrom" && c.Operator == ConditionOperator.OnOrBefore && c.Values.SequenceEqual(new object[] { latest }));
            conditions.Should().Contain(c => c.Attribute == "sprk_effectiveto" && c.Operator == ConditionOperator.OnOrAfter && c.Values.SequenceEqual(new object[] { earliest }));
        }

        call.Conditions.Should().Contain(c => c.Attribute == "statecode" && c.Values.SequenceEqual(new object[] { 0 }));
    }

    [Fact]
    public async Task WithNoCoreRecord_TheRateQueryAsksForFirmWideCardsOnly()
    {
        GivenRoster(Person("Jane Doe"));

        await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        var link = _calls.Single(c => c.Entity == "sprk_ratecardrate").LinkConditions;
        link.Where(c => c.Operator == ConditionOperator.Equal).Select(c => c.Attribute).Should()
            .NotIntersectWith(["sprk_matter", "sprk_project", "sprk_workassignment"]);
    }

    [Fact]
    public async Task TheRateQuery_UsesTheColumnOfTheCoreRecordsKind()
    {
        GivenRoster(Person("Jane Doe"));
        var workAssignmentId = Guid.NewGuid();

        await CreateService().ResolveAsync(Invoice(
            [Fee("Jane Doe", Day, 1m, 100m, 100m)], core: new CoreRecordReference(CoreRecordKind.WorkAssignment, workAssignmentId)));

        _calls.Single(c => c.Entity == "sprk_ratecardrate").LinkConditions.Should()
            .Contain(c => c.Attribute == "sprk_workassignment" && c.Operator == ConditionOperator.Equal && c.Values.SequenceEqual(new object[] { workAssignmentId }));
    }

    [Fact]
    public async Task ARateRowOfADraftCard_ReturnedAnyway_IsNeverUsed()
    {
        GivenRoster(Person("Jane Doe"));
        GivenRates(Rate(500m, classification: Partner, cardStatus: Draft));

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 2m, 500m, 1000m)]));

        result.Lines[0].Rate.Status.Should().Be(RateResolutionStatus.NotFound);
        result.Lines[0].Fields[ResolutionFieldNames.Rate].Reference.Should().Be(ReferenceOutcome.Missing);
        result.Lines[0].Fields[ResolutionFieldNames.Rate].Combined.Should().BeLessThanOrEqualTo(new EbillingSettings().ReferenceMissingCap);
    }

    [Fact]
    public async Task ARateRowOfAnotherFirmsCard_OrAnInactiveCard_OrAnInactiveRate_ReturnedAnyway_IsNeverUsed()
    {
        GivenRoster(Person("Jane Doe"));
        GivenRates(
            Rate(500m, classification: Partner, firm: OtherFirm),
            Rate(500m, classification: Partner, cardState: 1),
            Rate(500m, classification: Partner, rateState: 1));

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 2m, 500m, 1000m)]));

        result.Lines[0].Rate.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public async Task ACardScopedToAnotherMatter_IsNotUsed_AndOneScopedToThisMatterOverridesTheFirmCard()
    {
        var timekeeperId = Guid.NewGuid();
        GivenRoster(Person("Jane Doe", id: timekeeperId));
        GivenRates(
            Rate(400m, classification: Partner),
            Rate(999m, timekeeper: timekeeperId, scopeColumn: "sprk_matter", scopeId: OtherMatterId),
            Rate(450m, classification: Partner, scopeColumn: "sprk_matter", scopeId: MatterId));

        var result = await CreateService().ResolveAsync(Invoice(
            [Fee("Jane Doe", Day, 2m, 450m, 900m)], core: new CoreRecordReference(CoreRecordKind.Matter, MatterId)));

        result.Lines[0].Rate.ApprovedRate.Should().Be(450m);
        result.Lines[0].Rate.Scope.Should().Be(RateCardScope.CoreRecord);
    }

    [Fact]
    public async Task TheRateInForceOnEachLinesOwnDate_IsUsed_WithRateDatesReadAsDays()
    {
        var dayBefore = Day.AddDays(-1);
        GivenRoster(Person("Jane Doe"));
        GivenRates(
            Rate(400m, classification: Partner, rateTo: dayBefore.ToDateTime(TimeOnly.MinValue)),
            Rate(450m, classification: Partner, rateFrom: Day.ToDateTime(TimeOnly.MinValue)));

        var result = await CreateService().ResolveAsync(Invoice(
            [Fee("Jane Doe", dayBefore, 1m, 400m, 400m), Fee("Jane Doe", Day, 1m, 450m, 450m)]));

        result.Lines.Select(l => l.Rate.ApprovedRate).Should().Equal(400m, 450m);
    }

    [Fact]
    public async Task TwoRatesOfDifferentAmounts_AtTheWinningTier_AreAmbiguous_NeitherIsPicked_AndTheRateFieldIsReferenceMissing()
    {
        GivenRoster(Person("Jane Doe"));
        GivenRates(Rate(400m, classification: Partner), Rate(450m, classification: Partner));

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 2m, 400m, 800m)]));

        result.Lines[0].Rate.Status.Should().Be(RateResolutionStatus.Ambiguous);
        result.Lines[0].Rate.ApprovedRate.Should().BeNull();
        result.Lines[0].Fields[ResolutionFieldNames.Rate].Reference.Should().Be(ReferenceOutcome.Missing);
    }

    [Fact]
    public async Task TheRateReadIsSkipped_WhenNoLineMatchedAPersonOnADate()
    {
        GivenRoster(Person("Jane Doe"));

        await CreateService().ResolveAsync(Invoice(
            [Fee("Nobody Here", Day, 1m, 100m, 100m), Fee("Jane Doe", null, 1m, 100m, 100m)]));

        _calls.Select(c => c.Entity).Should().Equal("sprk_timekeeper");
    }

    // ── Bounded reads ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheNumberOfReads_DoesNotDependOnTheNumberOfLines()
    {
        _vectors["jon smith"] = [1f, 0f, 0f, 0f];
        _vectors["jonathan smith"] = [0.95f, MathF.Sqrt(1 - 0.95f * 0.95f), 0f, 0f];
        GivenRoster(Person("Jane Doe"), Person("Jonathan Smith"), Person("David Lockhart"));
        GivenRates(Rate(100m, classification: Partner));

        InvoiceResolutionInput WithLines(int count) => Invoice(
            Enumerable.Range(0, count)
                .Select(i => Fee(i % 3 == 0 ? "Jane Doe" : i % 3 == 1 ? "Jon Smith" : "DL", Day.AddDays(i % 5), 1m, 100m, 100m))
                .ToList(),
            summary: ["David Lockhart"]);

        await CreateService().ResolveAsync(WithLines(1));
        var oneLine = _calls.Count;
        var oneLineEmbeds = _embedded.Count;
        _calls.Clear();
        _embedded.Clear();

        var result = await CreateService().ResolveAsync(WithLines(300));

        result.Lines.Should().HaveCount(300);
        _calls.Should().HaveCount(2);
        oneLine.Should().Be(2);
        _calls.Select(c => c.Entity).Should().BeEquivalentTo(["sprk_timekeeper", "sprk_ratecardrate"]);
        _embedded.Should().HaveCount(2, "each distinct text is embedded once per invoice");
        oneLineEmbeds.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task AnInvoiceWithNoFeeLines_ReadsNothing()
    {
        await CreateService().ResolveAsync(Invoice([Expense(1m, 10m, 10m)], expenses: 10m));

        _calls.Should().BeEmpty();
        _embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task FeeLinesWithNoNameOrNoDate_ReadNothing()
    {
        var result = await CreateService().ResolveAsync(Invoice(
            [Fee(null, Day, 1m, 100m, 100m), Fee("Jane Doe", null, 1m, 100m, 100m)]));

        _calls.Should().BeEmpty();
        result.ApprovedTimekeeperFindingInputs.Select(f => f.Reason).Should().Equal(
            TimekeeperUnmatchedReason.NameMissing, TimekeeperUnmatchedReason.LineDateMissing);
    }

    [Fact]
    public async Task ARosterThatSpansPages_IsReadInFull_AndEveryPageIsAskedForWithTheCookieOfThePreviousOne()
    {
        var onPageTwo = Person("Jane Doe");
        _rosterPages = [[Person("John Roe")], [Person("Maria Soto")], [onPageTwo]];

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        var calls = _calls.Where(c => c.Entity == "sprk_timekeeper").ToList();
        calls.Select(c => c.Page).Should().Equal(1, 2, 3);
        calls.Select(c => c.Cookie).Should().Equal(null, "cookie1", "cookie2");
        result.Lines[0].Timekeeper!.TimekeeperId.Should().Be(onPageTwo.Id);
    }

    [Fact]
    public async Task ARateSetThatSpansPages_IsReadInFull()
    {
        var timekeeperId = Guid.NewGuid();
        GivenRoster(Person("Jane Doe", id: timekeeperId));
        _ratePages = [[Rate(1m, timekeeper: Guid.NewGuid())], [Rate(500m, timekeeper: timekeeperId)]];

        var result = await CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 2m, 500m, 1000m)]));

        _calls.Where(c => c.Entity == "sprk_ratecardrate").Select(c => c.Page).Should().Equal(1, 2);
        result.Lines[0].Rate.ApprovedRate.Should().Be(500m);
    }

    // ── Failure, cancellation, arguments, logging ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADataverseFailure_IsAnError_NotAResolutionMadeFromHalfTheRoster()
    {
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));

        var act = () => CreateService().ResolveAsync(Invoice([Fee("Jane Doe", Day, 1m, 100m, 100m)]));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dataverse down");
    }

    [Fact]
    public async Task ASettingsOutage_IsAnError()
    {
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("settings unavailable"));

        var act = () => CreateService().ResolveAsync(CleanInvoice());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ACancelledCaller_StopsTheResolution()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => CreateService().ResolveAsync(CleanInvoice(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _calls.Should().BeEmpty("no read starts for a caller that has already gone");
    }

    [Fact]
    public async Task AnInputWithNoFirm_IsRejected_BecauseNoRosterOrRateCardCouldBeChosen()
    {
        var input = Invoice([Fee("Jane Doe", Day, 1m, 100m, 100m)]) with { FirmId = Guid.Empty };

        var act = () => CreateService().ResolveAsync(input);

        await act.Should().ThrowAsync<ArgumentException>();
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheLog_CarriesCountsOnly_NeverNamesOrAmounts()
    {
        GivenRoster(Person("Jane Doe"));
        await CreateService().ResolveAsync(Invoice(
            [Fee("Jane Doe", Day, 1m, 123456.78m, 123456.78m), Fee("Zebediah Quux", Day, 1m, 100m, 100m)], summary: ["Hilda Brandt"]));

        _log.Messages.Should().NotBeEmpty();
        _log.Messages.Should().OnlyContain(m =>
            !m.Contains("Jane") && !m.Contains("Doe") && !m.Contains("Zebediah") && !m.Contains("Quux")
            && !m.Contains("Hilda") && !m.Contains("123456"));
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Condition(string Attribute, ConditionOperator Operator, object[] Values);

    /// <summary>What one query asked for, copied when it was received (the service reuses and mutates one query across pages).</summary>
    private sealed record Call(
        string Entity,
        int Page,
        string? Cookie,
        int PageSize,
        IReadOnlyList<Condition> Conditions,
        string? LinkEntity,
        IReadOnlyList<Condition> LinkConditions)
    {
        public static Call Of(QueryExpression query) => new(
            query.EntityName,
            query.PageInfo.PageNumber,
            query.PageInfo.PagingCookie,
            query.PageInfo.Count,
            Flatten(query.Criteria).ToList(),
            query.LinkEntities.SingleOrDefault()?.LinkToEntityName,
            query.LinkEntities.SingleOrDefault() is { } link ? Flatten(link.LinkCriteria).ToList() : []);

        private static IEnumerable<Condition> Flatten(FilterExpression filter)
        {
            foreach (var condition in filter.Conditions)
            {
                yield return new Condition(condition.AttributeName, condition.Operator, condition.Values.Cast<object>().ToArray());
            }

            foreach (var child in filter.Filters)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
