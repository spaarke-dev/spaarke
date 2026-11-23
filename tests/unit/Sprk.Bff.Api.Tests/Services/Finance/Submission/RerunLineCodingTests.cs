using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 095d-2a-1 (EB-D-186, owner Q3): the re-run's coding step sends a line to the AI coding service only when its code set
/// changes once the firm's own task codes count (FR-22 step 1); every other line keeps the coding intake stored. An uncoded invoice keeps its
/// stored coding hold unless step 1 now names a set.
/// </summary>
public sealed class RerunLineCodingTests
{
    private static readonly Guid StoredSet = Guid.Parse("095d2a10-0000-4000-8000-0000000000e1");
    private static readonly Guid FirmSet = Guid.Parse("04900000-0000-4000-8000-0000000000e1"); // FakeCoding's set

    private static LineCodingRequest Request(int lines = 2) => new()
    {
        CoreRecord = new CodingCoreRecord(CodingCoreRecord.Matter, Guid.Parse("095d2a10-0000-4000-8000-0000000000a1")),
        IncludeFirmCodes = true,
        Lines = Enumerable.Range(0, lines).Select(i => new InvoiceLineToCode
        {
            LineKey = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LineType = InvoiceLineType.Fee,
            Narrative = "Drafted the motion.",
            BilledTaskCode = "L110",
        }).ToList(),
    };

    private static LineCodingResult StoredLine(int index, Guid set) => new()
    {
        LineKey = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Status = LineCodingStatus.Coded,
        Task = new LineCodeValue("L120", "L110", true),
        Activity = new LineCodeValue("A101", null, false),
        Expense = new LineCodeValue(null, null, false),
        Confidence = 0.8m,
        Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.VagueDescription, Confidence = 0.9m, QuotedText = "the motion" }],
        RefusedCodes = [],
        CodeSetId = set,
        CodeSetName = "Stored set",
        CodeSetSource = CodeSetSource.MatterType,
    };

    private static StoredCoding Coded(params Guid[] setPerLine) => new(
        new InvoiceCodingResult
        {
            CodeSet = new ResolvedCodeSet(setPerLine[0], "Stored set", CodeSetSource.MatterType, [], [], []),
            Lines = setPerLine.Select((set, i) => StoredLine(i, set)).ToList(),
            ChunkCount = 0,
        },
        null,
        null,
        setPerLine.Length);

    private static Func<IReadOnlyList<InvoiceLineToCode>, CancellationToken, Task<ResolvedCodeSet?>> Step1(Guid? set, Action? asked = null) => (_, _) =>
    {
        asked?.Invoke();
        return Task.FromResult(set is { } id ? new ResolvedCodeSet(id, "Firm set", CodeSetSource.FirmCodes, [], [], []) : null);
    };

    [Fact(DisplayName = "095d-2a-1 (Q3) must-not-fire: step 1 names no set, so no line is sent to the AI and the stored coding (quotes included) is kept")]
    public async Task NoFirmCodeSet_KeepsTheStoredCoding_WithoutAnAiCall()
    {
        var facade = new InvoiceSubmissionProcessorTests.FakeCoding();
        var stored = Coded(StoredSet, StoredSet);
        var sut = new RerunLineCoding(facade, Step1(null), stored, NullLogger.Instance);

        var result = await sut.CodeLinesAsync(Request());

        facade.Request.Should().BeNull("no line was sent to the coding service");
        sut.RecodedLineCount.Should().Be(0);
        result.Should().BeSameAs(stored.Coded);
        result.Lines.SelectMany(l => l.Observations).Should().OnlyContain(o => o.QuotedText == "the motion");
    }

    [Fact(DisplayName = "095d-2a-1 (Q3) must-not-fire: step 1 names the set the lines were coded with, so no line is sent to the AI")]
    public async Task TheSameCodeSet_KeepsTheStoredCoding_WithoutAnAiCall()
    {
        var facade = new InvoiceSubmissionProcessorTests.FakeCoding();
        var asked = 0;
        var sut = new RerunLineCoding(facade, Step1(StoredSet, () => asked++), Coded(StoredSet, StoredSet), NullLogger.Instance);

        var result = await sut.CodeLinesAsync(Request());

        asked.Should().Be(1, "step 1 is asked (Dataverse reads only)");
        facade.Request.Should().BeNull();
        result.Lines.Should().OnlyContain(l => l.CodeSetId == StoredSet);
    }

    [Fact(DisplayName = "095d-2a-1 (Q3) must-fire: step 1 names another set, so exactly the lines coded with a different set are re-coded through the coding service")]
    public async Task AChangedCodeSet_RecodesOnlyTheLinesWhoseSetChanges()
    {
        var facade = new InvoiceSubmissionProcessorTests.FakeCoding();

        // Line 1 already carries the firm's set (a mix intake never writes, used here to show the rule is per line).
        var sut = new RerunLineCoding(facade, Step1(FirmSet), Coded(StoredSet, FirmSet), NullLogger.Instance);

        var result = await sut.CodeLinesAsync(Request());

        facade.Request!.Lines.Select(l => l.LineKey).Should().Equal("0");
        sut.RecodedLineCount.Should().Be(1);
        result.CodeSet.CodeSetId.Should().Be(FirmSet);
        result.Lines.Select(l => (l.LineKey, l.CodeSetId)).Should().Equal(("0", FirmSet), ("1", FirmSet));
        result.Lines[0].Task.Code.Should().Be("L110", "the re-coded line takes the service's codes");
        result.Lines[1].Task.Code.Should().Be("L120", "the unchanged line keeps its stored codes");
    }

    [Fact(DisplayName = "095d-2a-1: a re-code whose service resolves another set than step 1 named is a fault (nothing is mixed, nothing written)")]
    public async Task ARecodeUnderAnotherSet_Faults()
    {
        var sut = new RerunLineCoding(new InvoiceSubmissionProcessorTests.FakeCoding(), Step1(Guid.NewGuid()), Coded(StoredSet, StoredSet), NullLogger.Instance);

        await sut.Invoking(s => s.CodeLinesAsync(Request())).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory(DisplayName = "095d-2a-1 (Q3): an uncoded invoice keeps its stored coding hold without an AI call when step 1 names no set")]
    [InlineData(SubmissionReasonCodes.CodingNoCodeSetHold, LineCodingException.NoCodeSet, LineCodingException.NoCodeSet)]
    [InlineData(SubmissionReasonCodes.CodingUnavailableHold, "ai.feature_disabled", "ai.feature_disabled")]
    public async Task AnUncodedInvoice_KeepsItsHold_WhenNoFirmSetApplies(string hold, string cause, string raised)
    {
        var facade = new InvoiceSubmissionProcessorTests.FakeCoding();
        var sut = new RerunLineCoding(facade, Step1(null), new StoredCoding(null, hold, cause, 0), NullLogger.Instance);

        var thrown = await sut.Invoking(s => s.CodeLinesAsync(Request())).Should().ThrowAsync<LineCodingException>();

        thrown.Which.ReasonCode.Should().Be(raised, "the processor maps it to the same pipeline finding intake stored");
        facade.Request.Should().BeNull();
    }

    [Fact(DisplayName = "095d-2a-1 (Q3) must-fire: an uncoded invoice is coded through the service once step 1 names a set")]
    public async Task AnUncodedInvoice_IsCoded_WhenTheFirmsSetApplies()
    {
        var facade = new InvoiceSubmissionProcessorTests.FakeCoding();
        var sut = new RerunLineCoding(facade, Step1(FirmSet), new StoredCoding(null, SubmissionReasonCodes.CodingNoCodeSetHold, LineCodingException.NoCodeSet, 0), NullLogger.Instance);

        var result = await sut.CodeLinesAsync(Request());

        facade.Request!.Lines.Should().HaveCount(2);
        result.Lines.Should().OnlyContain(l => l.CodeSetId == FirmSet);
    }
}
