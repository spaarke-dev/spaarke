using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// Seam for the FR-22 code-set resolution order (ebilling-r1 task 047): the production <see cref="CodeSetResolver"/> runs against an
/// in-memory Dataverse that evaluates the queries it sends (<see cref="FakeCodingReferenceData"/>). Only the Dataverse boundary, the
/// settings row and the step-4 suggestion provider (task 048) are doubled.
/// </summary>
/// <remarks>
/// Each step is proved by a case where it wins AND a case where it is skipped, with a lower step set up to give a different answer,
/// so a resolver that ignored the order, or took the first mapping it found regardless of step, fails.
/// </remarks>
public sealed class CodeSetResolverSeamTests
{
    private readonly FakeCodingReferenceData _data = new();
    private readonly Mock<IEbillingSettingsReader> _settings = new();
    private readonly Mock<ICodeSetSuggestionProvider> _suggestions = new();

    public CodeSetResolverSeamTests()
    {
        GivenSettings(new EbillingSettings());
        _suggestions
            .Setup(s => s.SuggestCodeSetAsync(It.IsAny<CodeSetSuggestionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
    }

    private CodeSetResolver Sut => new(_data.Entities.Object, _settings.Object, _suggestions.Object, NullLogger<CodeSetResolver>.Instance);

    private void GivenSettings(EbillingSettings settings) =>
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

    private static InvoiceLineToCode Line(string key, string? billedTask = null, string narrative = "Reviewed the file") => new()
    {
        LineKey = key,
        LineType = InvoiceLineType.Fee,
        Narrative = narrative,
        BilledTaskCode = billedTask,
    };

    // ── Step 1: firm codes ──────────────────────────────────────────────────────

    [Fact]
    public async Task FirmTaskCodes_AllInOneSet_SelectThatSet_EvenWhenTheMatterTypeMapsAnother()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Counseling));

        var resolved = await Sut.ResolveAsync(core, [Line("1", "L110"), Line("2", "l210 "), Line("3", "L320")]);

        resolved.Source.Should().Be(CodeSetSource.FirmCodes);
        resolved.CodeSetId.Should().Be(utbms.Litigation);
        resolved.CodeSetName.Should().Be("UTBMS Litigation");
        resolved.NeedsReview.Should().BeFalse();
        _suggestions.Verify(s => s.SuggestCodeSetAsync(It.IsAny<CodeSetSuggestionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FirmTaskCodes_ThatBelongToTwoSets_AreNotDecisive_SoTheMatterTypeMappingApplies()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Counseling));

        // L120 is in both Litigation and Counseling: the codes alone cannot pick one.
        var resolved = await Sut.ResolveAsync(core, [Line("1", "L120")]);

        resolved.Source.Should().Be(CodeSetSource.MatterType);
        resolved.CodeSetId.Should().Be(utbms.Counseling);
    }

    [Fact]
    public async Task FirmTaskCodes_NotAllInOneSet_AreNotDecisive()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Counseling));

        // L110 is litigation only, C100 is counseling only: no set holds both.
        var resolved = await Sut.ResolveAsync(core, [Line("1", "L110"), Line("2", "C100")]);

        resolved.Source.Should().Be(CodeSetSource.MatterType);
    }

    [Fact]
    public async Task FirmTaskCodes_ThatMatchNoCode_AreNotDecisive()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Counseling));

        var resolved = await Sut.ResolveAsync(core, [Line("1", "ZZZ")]);

        resolved.Source.Should().Be(CodeSetSource.MatterType);
    }

    [Fact]
    public async Task FirmTaskCodes_OnlyCountAgainstTaskCodes_AnActivityCodeBilledAsTaskIsNotDecisive()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Counseling));

        // A101 exists, but as an Activity code; the firm put it in the task column.
        var resolved = await Sut.ResolveAsync(core, [Line("1", "A101")]);

        resolved.Source.Should().Be(CodeSetSource.MatterType);
    }

    [Fact]
    public async Task NoFirmCodes_SkipsStepOneWithoutQueryingCodes()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Counseling));

        var resolved = await Sut.ResolveAsync(core, [Line("1"), Line("2", "  ")]);

        resolved.Source.Should().Be(CodeSetSource.MatterType);
        _data.Queries.Should().NotContain(
            q => q.Criteria.Conditions.Any(c => c.Operator == ConditionOperator.In),
            "no firm code was billed, so the code table is never searched by code");
    }

    // ── Steps 2 and 3: the mappings ─────────────────────────────────────────────

    [Fact]
    public async Task MatterTypeMapping_WinsOverThePracticeAreaMapping()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord(
            "sprk_project",
            matterTypeId: _data.AddMatterType(utbms.Counseling),
            practiceAreaId: _data.AddPracticeArea(utbms.Litigation));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.MatterType);
        resolved.CodeSetId.Should().Be(utbms.Counseling);
    }

    [Fact]
    public async Task PracticeAreaMapping_AppliesWhenTheMatterTypeHasNoMapping()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord(
            "sprk_workassignment",
            matterTypeId: _data.AddMatterType(defaultSetId: null),
            practiceAreaId: _data.AddPracticeArea(utbms.Counseling));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.PracticeArea);
        resolved.CodeSetId.Should().Be(utbms.Counseling);
    }

    [Fact]
    public async Task PracticeAreaMapping_AppliesWhenTheRecordHasNoMatterType()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", practiceAreaId: _data.AddPracticeArea(utbms.Counseling));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.PracticeArea);
    }

    [Fact]
    public async Task AMappingToAnInactiveSet_IsSkipped_AndTheNextStepApplies()
    {
        var utbms = _data.SeedUtbms();
        var retired = _data.AddSet("UTBMS Retired", active: false);
        _data.AddCode(retired, "R100", BillingCodeKind.Task, "Retired code");
        var core = _data.AddCoreRecord(
            "sprk_matter",
            matterTypeId: _data.AddMatterType(retired),
            practiceAreaId: _data.AddPracticeArea(utbms.Counseling));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.PracticeArea);
        resolved.CodeSetId.Should().Be(utbms.Counseling);
    }

    [Fact]
    public async Task AMappingToASetWithNoTaskCodes_IsSkipped()
    {
        var utbms = _data.SeedUtbms();
        // The shared Activity set mapped by mistake: it has no task codes, so it cannot code a Fee line.
        var core = _data.AddCoreRecord(
            "sprk_matter",
            matterTypeId: _data.AddMatterType(utbms.Activity),
            practiceAreaId: _data.AddPracticeArea(utbms.Litigation));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.PracticeArea);
    }

    // ── Step 4: the suggestion hook (task 048) ──────────────────────────────────

    [Fact]
    public async Task AiSuggestion_AppliesWhenNoMappingDoes_IsFlaggedForReview_AndGetsTheRecordsTypeIds()
    {
        var utbms = _data.SeedUtbms();
        var matterType = _data.AddMatterType(defaultSetId: null);
        var practiceArea = _data.AddPracticeArea(defaultSetId: null);
        var core = _data.AddCoreRecord("sprk_matter", matterType, practiceArea);
        var lines = new[] { Line("1", narrative: "Reviewed the lease") };
        CodeSetSuggestionContext? seen = null;
        _suggestions
            .Setup(s => s.SuggestCodeSetAsync(It.IsAny<CodeSetSuggestionContext>(), It.IsAny<CancellationToken>()))
            .Callback((CodeSetSuggestionContext c, CancellationToken _) => seen = c)
            .ReturnsAsync(utbms.Counseling);

        var resolved = await Sut.ResolveAsync(core, lines);

        resolved.Source.Should().Be(CodeSetSource.AiSuggestion);
        resolved.NeedsReview.Should().BeTrue("FR-22: an AI-suggested set is flagged for review");
        resolved.CodeSetId.Should().Be(utbms.Counseling);
        seen.Should().NotBeNull();
        seen!.CoreRecord.Should().Be(core);
        seen.MatterTypeId.Should().Be(matterType);
        seen.PracticeAreaId.Should().Be(practiceArea);
        seen.Lines.Should().BeEquivalentTo(lines);
    }

    [Fact]
    public async Task AiSuggestion_OfASetThatDoesNotExist_CannotPassTheGuard_AndTheSettingsDefaultApplies()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter");
        GivenSettings(new EbillingSettings { DefaultCodeSetId = utbms.Litigation });
        _suggestions
            .Setup(s => s.SuggestCodeSetAsync(It.IsAny<CodeSetSuggestionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.SettingsDefault);
    }

    [Fact]
    public async Task AFailingSuggestionProvider_PropagatesInsteadOfFallingBackToTheDefault()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter");
        GivenSettings(new EbillingSettings { DefaultCodeSetId = utbms.Litigation });
        _suggestions
            .Setup(s => s.SuggestCodeSetAsync(It.IsAny<CodeSetSuggestionContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        await FluentActions.Awaiting(() => Sut.ResolveAsync(core, [Line("1")]))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("provider down");
    }

    // ── Step 5: the settings default ────────────────────────────────────────────

    [Fact]
    public async Task SettingsDefaultCodeSet_AppliesLast()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(null), practiceAreaId: _data.AddPracticeArea(null));
        GivenSettings(new EbillingSettings { DefaultCodeSetId = utbms.Counseling });

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.SettingsDefault);
        resolved.CodeSetId.Should().Be(utbms.Counseling);
        resolved.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task WithNoSettingsDefaultSet_TheSetNamedUtbmsLitigationIsTheDefault()
    {
        var utbms = _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter");

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.Source.Should().Be(CodeSetSource.SettingsDefault);
        resolved.CodeSetId.Should().Be(utbms.Litigation);
    }

    [Fact]
    public async Task WhenNothingResolves_ThrowsNoCodeSet()
    {
        // Sets exist but none is mapped and there is no "UTBMS Litigation" and no settings default.
        var other = _data.AddSet("Firm Custom");
        _data.AddCode(other, "X100", BillingCodeKind.Task, "Custom");
        var core = _data.AddCoreRecord("sprk_matter");

        var ex = await FluentActions.Awaiting(() => Sut.ResolveAsync(core, [Line("1")])).Should().ThrowAsync<LineCodingException>();

        ex.Which.ReasonCode.Should().Be(LineCodingException.NoCodeSet);
    }

    [Fact]
    public async Task WhenTheSettingsDefaultSetIsMissingOrInactive_ThrowsNoCodeSet()
    {
        _data.SeedUtbms();
        var core = _data.AddCoreRecord("sprk_matter");
        GivenSettings(new EbillingSettings { DefaultCodeSetId = Guid.NewGuid() });

        var ex = await FluentActions.Awaiting(() => Sut.ResolveAsync(core, [Line("1")])).Should().ThrowAsync<LineCodingException>();

        ex.Which.ReasonCode.Should().Be(LineCodingException.NoCodeSet);
    }

    // ── What a resolved set carries ─────────────────────────────────────────────

    [Fact]
    public async Task ResolvedSet_CarriesTheActiveCodesOfEachKind_WithDescriptionAndGuidance_FromTheRightSets()
    {
        var utbms = _data.SeedUtbms();
        _data.AddCode(utbms.Litigation, "L999", BillingCodeKind.Task, "Retired task code", "Do not use.", active: false);
        _data.AddCode(utbms.Activity, "A199", BillingCodeKind.Activity, "Retired activity", active: false);
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(utbms.Litigation));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.TaskCodes.Select(c => c.Code).Should().Equal("L110", "L120", "L210", "L320");
        resolved.TaskCodes[0].Should().BeEquivalentTo(new InvoiceCodeOption("L110", "Fact Investigation/Development", "Use for gathering facts."));
        resolved.TaskCodes[3].Guidance.Should().BeNull("a code with no guidance text sends null, not an empty string");
        resolved.ActivityCodes.Select(c => c.Code).Should().Equal("A101", "A102", "A103", "A104");
        resolved.ExpenseCodes.Select(c => c.Code).Should().Equal("E101", "E112");
        resolved.TryGetCode(BillingCodeKind.Task, "L999", out _).Should().BeFalse("an inactive code is not allowed");
        resolved.TryGetCode(BillingCodeKind.Activity, "A199", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ASetThatHoldsItsOwnActivityAndExpenseCodes_UsesThemInsteadOfTheSharedSets()
    {
        _data.SeedUtbms();
        var custom = _data.AddSet("Firm Custom");
        _data.AddCode(custom, "F1", BillingCodeKind.Task, "Custom task");
        _data.AddCode(custom, "FA1", BillingCodeKind.Activity, "Custom activity");
        _data.AddCode(custom, "FE1", BillingCodeKind.Expense, "Custom expense");
        var core = _data.AddCoreRecord("sprk_matter", matterTypeId: _data.AddMatterType(custom));

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.CodeSetId.Should().Be(custom);
        resolved.ActivityCodes.Select(c => c.Code).Should().Equal("FA1");
        resolved.ExpenseCodes.Select(c => c.Code).Should().Equal("FE1");
        resolved.TryGetCode(BillingCodeKind.Activity, "A101", out _).Should().BeFalse("the shared set is only a fallback");
    }

    [Fact]
    public async Task WhenTheSharedActivityAndExpenseSetsDoNotExist_ThoseListsAreEmpty_NotAFailure()
    {
        var litigation = _data.AddSet("UTBMS Litigation");
        _data.AddCode(litigation, "L110", BillingCodeKind.Task, "Fact Investigation");
        var core = _data.AddCoreRecord("sprk_matter");

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.TaskCodes.Should().ContainSingle();
        resolved.ActivityCodes.Should().BeEmpty();
        resolved.ExpenseCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASetLargerThanOnePage_IsLoadedInFull()
    {
        var big = _data.AddSet("UTBMS Litigation");
        // More than the 5000 rows Dataverse returns for a query that sets no page size, so a resolver that does not page loses codes.
        const int codeCount = 5203;
        for (var i = 0; i < codeCount; i++)
        {
            _data.AddCode(big, $"T{i:0000}", BillingCodeKind.Task, $"Task {i}");
        }

        var core = _data.AddCoreRecord("sprk_matter");

        var resolved = await Sut.ResolveAsync(core, [Line("1")]);

        resolved.TaskCodes.Should().HaveCount(codeCount, "paging follows MoreRecords/PagingCookie until the set is complete");
        resolved.TryGetCode(BillingCodeKind.Task, $"T{codeCount - 1:0000}", out _).Should().BeTrue();
    }

    // ── Argument contract ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_mattertype_ref")]
    [InlineData("account")]
    public async Task AnEntityThatIsNotACoreRecord_IsRefused_BeforeAnyRead(string entity)
    {
        _data.SeedUtbms();

        await FluentActions.Awaiting(() => Sut.ResolveAsync(new CodingCoreRecord(entity, Guid.NewGuid()), [Line("1")]))
            .Should().ThrowAsync<ArgumentException>();

        _data.QueryCount.Should().Be(0);
        _data.Entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
