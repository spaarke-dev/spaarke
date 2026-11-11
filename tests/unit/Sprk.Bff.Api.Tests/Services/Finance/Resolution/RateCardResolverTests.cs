using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Resolution;

/// <summary>
/// <see cref="RateCardResolver"/> (ebilling-r1 task 046, FR-23, FR-08): the rate in force for a fee line.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the approved rate is what FR-08 compares a billed rate against, so a rate taken from a Draft card, another
/// firm's card or another matter's card would produce wrong findings. "Disagreement is not resolved by guessing" is pinned too.
/// </remarks>
public sealed class RateCardResolverTests
{
    private static readonly Guid Firm = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherFirm = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Tk = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherTk = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly CoreRecordReference Matter = new(CoreRecordKind.Matter, Guid.Parse("cccccccc-0000-0000-0000-000000000001"));
    private static readonly CoreRecordReference OtherMatter = new(CoreRecordKind.Matter, Guid.Parse("cccccccc-0000-0000-0000-000000000002"));
    private static readonly CoreRecordReference Project = new(CoreRecordKind.Project, Matter.Id);
    private static readonly DateOnly Day = new(2026, 6, 15);
    private const int Partner = 1;
    private const int Associate = 2;

    private static TimekeeperResolution Matched(int? classification = Partner) =>
        new() { TimekeeperId = Tk, Stage = TimekeeperMatchStage.Exact, MatchConfidence = 1m, Classification = classification };

    private static readonly TimekeeperResolution Unmatched =
        new() { Stage = TimekeeperMatchStage.None, Reason = TimekeeperUnmatchedReason.NotOnRoster };

    private static int _next;

    /// <summary>A rate row that applies to <see cref="Matched"/> on <see cref="Day"/> unless a parameter says otherwise.</summary>
    private static RateRow Row(
        decimal rate,
        Guid? timekeeper = null,
        int? classification = null,
        CoreRecordReference? scope = null,
        bool published = true,
        Guid? firm = null,
        bool rateActive = true,
        DateOnly? cardFrom = null,
        DateOnly? cardTo = null,
        DateOnly? rateFrom = null,
        DateOnly? rateTo = null,
        Guid? cardId = null,
        IReadOnlyList<CoreRecordReference>? scopes = null)
    {
        var n = Interlocked.Increment(ref _next);
        return new RateRow(
            RateId: new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1),
            CardId: cardId ?? new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2),
            CardFirmId: firm ?? Firm,
            CardPublished: published,
            CardScopes: scopes ?? (scope is null ? [] : [scope]),
            CardEffectiveFrom: cardFrom,
            CardEffectiveTo: cardTo,
            RateActive: rateActive,
            RateEffectiveFrom: rateFrom,
            RateEffectiveTo: rateTo,
            TimekeeperId: timekeeper,
            Classification: classification,
            ApprovedRate: rate,
            RackRate: rate + 100m);
    }

    private static RateResolution Resolve(IReadOnlyList<RateRow> rows, CoreRecordReference? core = null, TimekeeperResolution? timekeeper = null, DateOnly? date = null) =>
        RateCardResolver.Resolve(rows, Firm, core, timekeeper ?? Matched(), date ?? Day);

    // ── What applies ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ATimekeeperRate_OnAFirmCard_IsFound_WithItsRackRateAndIdentity()
    {
        var row = Row(450m, timekeeper: Tk);

        var result = Resolve([row]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(450m);
        result.RackRate.Should().Be(550m);
        result.RateCardId.Should().Be(row.CardId);
        result.RateId.Should().Be(row.RateId);
        result.Scope.Should().Be(RateCardScope.Firm);
        result.Basis.Should().Be(RateBasis.Timekeeper);
    }

    [Fact]
    public void AClassificationRate_IsUsed_WhenNoRateNamesTheTimekeeper()
    {
        var result = Resolve([Row(300m, classification: Partner)]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(300m);
        result.Basis.Should().Be(RateBasis.Classification);
    }

    [Fact]
    public void AClassificationRate_ForAnotherClassification_IsNotUsed()
    {
        var result = Resolve([Row(300m, classification: Associate)]);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
        result.Reason.Should().Be(RateNotFoundReason.NoRateInForce);
    }

    [Fact]
    public void AClassificationRate_IsNotUsed_WhenTheTimekeeperHasNoClassification()
    {
        var result = Resolve([Row(300m, classification: Partner)], timekeeper: Matched(classification: null));

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void ARateForAnotherTimekeeper_IsNotUsed_EvenOnTheSameCardAndClassification()
    {
        var result = Resolve([Row(900m, timekeeper: OtherTk, classification: Partner)]);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    // ── Precedence ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Precedence_CoreRecordCardTimekeeperRate_BeatsEveryOtherTier()
    {
        var winner = Row(1m, timekeeper: Tk, scope: Matter);
        var rows = new[]
        {
            Row(2m, classification: Partner, scope: Matter),
            Row(3m, timekeeper: Tk),
            Row(4m, classification: Partner),
            winner,
        };

        var result = Resolve(rows, Matter);

        result.ApprovedRate.Should().Be(1m);
        result.Scope.Should().Be(RateCardScope.CoreRecord);
        result.Basis.Should().Be(RateBasis.Timekeeper);
        result.RateId.Should().Be(winner.RateId);
    }

    [Fact]
    public void Precedence_TheCardScopeDecidesBeforeTheBasis_ACoreRecordClassificationRateBeatsAFirmTimekeeperRate()
    {
        var result = Resolve(
            [Row(3m, timekeeper: Tk), Row(2m, classification: Partner, scope: Matter), Row(4m, classification: Partner)],
            Matter);

        result.ApprovedRate.Should().Be(2m);
        result.Scope.Should().Be(RateCardScope.CoreRecord);
        result.Basis.Should().Be(RateBasis.Classification);
    }

    [Fact]
    public void Precedence_OnAFirmCard_ATimekeeperRateBeatsAClassificationRate()
    {
        var result = Resolve([Row(4m, classification: Partner), Row(3m, timekeeper: Tk)]);

        result.ApprovedRate.Should().Be(3m);
        result.Basis.Should().Be(RateBasis.Timekeeper);
    }

    [Fact]
    public void Precedence_FallsThroughToTheFirmCard_WhenTheCoreRecordCardHasNoRateForThisPerson()
    {
        var result = Resolve(
            [Row(2m, timekeeper: OtherTk, scope: Matter), Row(4m, classification: Partner)],
            Matter);

        result.ApprovedRate.Should().Be(4m);
        result.Scope.Should().Be(RateCardScope.Firm);
    }

    [Fact]
    public void Precedence_DoesNotDependOnTheOrderOfTheRows()
    {
        var rows = new[]
        {
            Row(1m, timekeeper: Tk, scope: Matter),
            Row(2m, classification: Partner, scope: Matter),
            Row(3m, timekeeper: Tk),
            Row(4m, classification: Partner),
        };

        var expected = Resolve(rows, Matter);
        Resolve(rows.Reverse().ToArray(), Matter).Should().BeEquivalentTo(expected);
        Resolve(rows.OrderBy(r => r.RateId).ToArray(), Matter).Should().BeEquivalentTo(expected);
    }

    // ── Scope ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ACardScopedToAnotherCoreRecord_NeverApplies()
    {
        var result = Resolve([Row(2m, timekeeper: Tk, scope: OtherMatter)], Matter);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void AScopedCard_DoesNotApply_WhenTheInvoiceHasNoCoreRecord()
    {
        var result = Resolve([Row(2m, timekeeper: Tk, scope: Matter)], core: null);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void AFirmCard_AppliesWhenTheInvoiceHasNoCoreRecord()
    {
        Resolve([Row(2m, timekeeper: Tk)], core: null).Status.Should().Be(RateResolutionStatus.Found);
    }

    [Fact]
    public void ACardScopedToAProject_DoesNotApplyToAMatterWithTheSameId()
    {
        // Same id, different kind: they are different records.
        var result = Resolve([Row(2m, timekeeper: Tk, scope: Project)], Matter);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void ACardScopedToSeveralCoreRecords_IsNotUsed_BecauseTheWriterForbidsIt()
    {
        var result = Resolve([Row(2m, timekeeper: Tk, scopes: [Matter, OtherMatter])], Matter);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    // ── Excluded rows ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ACardThatIsNotPublished_IsNeverUsed()
    {
        Resolve([Row(2m, timekeeper: Tk, published: false)]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void AnotherFirmsCard_IsNeverUsed()
    {
        Resolve([Row(2m, timekeeper: Tk, firm: OtherFirm)]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void ACardWithNoFirm_IsNeverUsed()
    {
        var row = Row(2m, timekeeper: Tk) with { CardFirmId = null };

        Resolve([row]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void AnInactiveRate_IsNeverUsed()
    {
        Resolve([Row(2m, timekeeper: Tk, rateActive: false)]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void ARateWithNoApprovedAmount_IsIgnored_NotTreatedAsZero()
    {
        var row = Row(2m, timekeeper: Tk) with { ApprovedRate = null };

        Resolve([row]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    // ── Dates (both ends inclusive, an empty end is open) ────────────────────────────────────────────────────────

    [Fact]
    public void CardDates_AreInclusiveAtBothEnds()
    {
        Resolve([Row(2m, timekeeper: Tk, cardFrom: Day, cardTo: Day)]).Status.Should().Be(RateResolutionStatus.Found);
        Resolve([Row(2m, timekeeper: Tk, cardFrom: Day.AddDays(1))]).Status.Should().Be(RateResolutionStatus.NotFound);
        Resolve([Row(2m, timekeeper: Tk, cardTo: Day.AddDays(-1))]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void RateDates_AreInclusiveAtBothEnds()
    {
        Resolve([Row(2m, timekeeper: Tk, rateFrom: Day, rateTo: Day)]).Status.Should().Be(RateResolutionStatus.Found);
        Resolve([Row(2m, timekeeper: Tk, rateFrom: Day.AddDays(1))]).Status.Should().Be(RateResolutionStatus.NotFound);
        Resolve([Row(2m, timekeeper: Tk, rateTo: Day.AddDays(-1))]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void ARateWhoseCardIsInForce_ButWhoseOwnDatesAreNot_IsNotUsed()
    {
        var result = Resolve([Row(2m, timekeeper: Tk, cardFrom: Day.AddDays(-30), cardTo: Day.AddDays(30), rateFrom: Day.AddDays(1))]);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void ARateFromAnEarlierPeriod_IsReplacedByTheOneInForceOnTheLineDate()
    {
        var old = Row(300m, timekeeper: Tk, rateFrom: new DateOnly(2025, 1, 1), rateTo: new DateOnly(2025, 12, 31));
        var current = Row(350m, timekeeper: Tk, rateFrom: new DateOnly(2026, 1, 1));

        Resolve([old, current], date: new DateOnly(2025, 12, 31)).ApprovedRate.Should().Be(300m);
        Resolve([old, current], date: new DateOnly(2026, 1, 1)).ApprovedRate.Should().Be(350m);
    }

    // ── Disagreement and absence ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoRatesOfDifferentAmounts_AtTheWinningTier_AreAmbiguous_AndNoRateIsPicked()
    {
        var result = Resolve([Row(400m, timekeeper: Tk), Row(450m, timekeeper: Tk)]);

        result.Status.Should().Be(RateResolutionStatus.Ambiguous);
        result.ApprovedRate.Should().BeNull();
        result.RateId.Should().BeNull();
    }

    [Fact]
    public void TwoRatesOfTheSameAmount_AtTheWinningTier_AreOneRate()
    {
        var a = Row(400m, timekeeper: Tk);
        var b = Row(400m, timekeeper: Tk);

        var result = Resolve([b, a]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(400m);
    }

    [Fact]
    public void ADisagreement_BelowTheWinningTier_DoesNotMatter()
    {
        var result = Resolve([Row(1m, timekeeper: Tk), Row(2m, classification: Partner), Row(3m, classification: Partner)]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(1m);
    }

    [Fact]
    public void AnUnmatchedTimekeeper_IsNotFound_NotAnException()
    {
        var result = Resolve([Row(2m, timekeeper: Tk), Row(3m, classification: Partner)], timekeeper: Unmatched);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
        result.Reason.Should().Be(RateNotFoundReason.TimekeeperUnmatched);
    }

    [Fact]
    public void ALineWithNoDate_IsNotFound_NotAnException()
    {
        var result = RateCardResolver.Resolve([Row(2m, timekeeper: Tk)], Firm, null, Matched(), lineDate: null);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
        result.Reason.Should().Be(RateNotFoundReason.LineDateMissing);
    }

    [Fact]
    public void NoRows_IsNotFound_NotAnException()
    {
        var result = Resolve([]);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
        result.Reason.Should().Be(RateNotFoundReason.NoRateInForce);
    }

    [Fact]
    public void Resolve_IsDeterministic()
    {
        RateRow[] rows = [Row(1m, timekeeper: Tk, scope: Matter), Row(1m, timekeeper: Tk, scope: Matter), Row(2m, classification: Partner)];

        Resolve(rows, Matter).Should().BeEquivalentTo(Resolve(rows, Matter));
    }
}
