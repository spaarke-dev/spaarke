using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Resolution;

/// <summary>
/// <see cref="RateCardResolver"/> (ebilling-r1 task 046, FR-23, FR-08; re-keyed to the Procedure Version by task 085, model 7.1 and 7.3): the
/// rate in force for a fee line.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the approved rate is what FR-08 compares a billed rate against, so a rate taken from a Draft card, another
/// firm's card or a card of a Procedure Version the resolver did not choose would produce wrong findings. The precedence by the resolver's
/// rank (Manual d0 &gt; Manual d1 &gt; Firm &gt; All records) and "disagreement is not resolved by guessing" are pinned too.
/// </remarks>
public sealed class RateCardResolverTests
{
    private static readonly Guid Firm = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherFirm = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Tk = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherTk = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly DateOnly Day = new(2026, 6, 15);
    private const int Partner = 1;
    private const int Associate = 2;

    // The resolver's rank key (model 6.5): Manual d0 > Manual d1 > Firm > All records, then FirmSpecific.
    private static readonly ProcedureRank ManualD0 = new(ProcedureLevel.ManualOwn, 0, false);
    private static readonly ProcedureRank ManualD1 = new(ProcedureLevel.ManualParent, 1, false);
    private static readonly ProcedureRank FirmLevel = new(ProcedureLevel.Firm, 0, true);
    private static readonly ProcedureRank AllRecords = new(ProcedureLevel.AllRecords, 0, false);
    private static readonly ProcedureRank AllRecordsFirmSpecific = new(ProcedureLevel.AllRecords, 0, true);

    /// <summary>A Procedure Version id that stands for a rank: the same rank is the same version in these tests.</summary>
    private static Guid VersionOf(ProcedureRank rank) =>
        new((int)rank.Level, (short)rank.Distance, rank.FirmSpecific ? (short)1 : (short)0, 0, 0, 0, 0, 0, 0, 0, 9);

    private static readonly Guid UnchosenVersion = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    /// <summary>The versions the resolver chose for the line date, with their ranks.</summary>
    private static IReadOnlyDictionary<Guid, ProcedureRank> Chosen(params ProcedureRank[] ranks) =>
        ranks.ToDictionary(VersionOf, r => r);

    private static TimekeeperResolution Matched(int? classification = Partner) =>
        new() { TimekeeperId = Tk, Stage = TimekeeperMatchStage.Exact, MatchConfidence = 1m, Classification = classification };

    private static readonly TimekeeperResolution Unmatched =
        new() { Stage = TimekeeperMatchStage.None, Reason = TimekeeperUnmatchedReason.NotOnRoster };

    private static int _next;

    /// <summary>A rate row that applies to <see cref="Matched"/> on <see cref="Day"/> under <see cref="AllRecords"/> unless a parameter says otherwise.</summary>
    private static RateRow Row(
        decimal rate,
        Guid? timekeeper = null,
        int? classification = null,
        ProcedureRank? rank = null,
        Guid? version = null,
        bool published = true,
        Guid? firm = null,
        bool rateActive = true,
        DateOnly? cardFrom = null,
        DateOnly? cardTo = null,
        DateOnly? rateFrom = null,
        DateOnly? rateTo = null,
        Guid? cardId = null)
    {
        var n = Interlocked.Increment(ref _next);
        return new RateRow(
            RateId: new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1),
            CardId: cardId ?? new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2),
            CardFirmId: firm ?? Firm,
            CardPublished: published,
            ProcedureVersionId: version ?? VersionOf(rank ?? AllRecords),
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

    private static RateResolution Resolve(
        IReadOnlyList<RateRow> rows, IReadOnlyDictionary<Guid, ProcedureRank>? chosen = null, TimekeeperResolution? timekeeper = null, DateOnly? date = null) =>
        RateCardResolver.Resolve(rows, Firm, chosen ?? Chosen(AllRecords), timekeeper ?? Matched(), date ?? Day);

    // ── What applies ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ATimekeeperRate_OnAChosenCard_IsFound_WithItsRackRateIdentityAndRank()
    {
        var row = Row(450m, timekeeper: Tk);

        var result = Resolve([row]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(450m);
        result.RackRate.Should().Be(550m);
        result.RateCardId.Should().Be(row.CardId);
        result.RateId.Should().Be(row.RateId);
        result.Rank.Should().Be(AllRecords);
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

    // ── Procedure Version: only the versions the resolver chose ──────────────────────────────────────────────────

    [Fact(DisplayName = "085 must-fire: a card of a Procedure Version the resolver did not choose is never used")]
    public void ACardOfAVersionTheResolverDidNotChoose_IsNeverUsed()
    {
        var result = Resolve([Row(2m, timekeeper: Tk, version: UnchosenVersion)]);

        result.Status.Should().Be(RateResolutionStatus.NotFound);
        result.Reason.Should().Be(RateNotFoundReason.NoRateInForce);
    }

    [Fact(DisplayName = "085 must-not-fire: the same card is used as soon as its version is among those chosen")]
    public void TheSameCard_IsUsed_OnceItsVersionIsChosen()
    {
        var row = Row(2m, timekeeper: Tk, version: UnchosenVersion);
        var chosen = new Dictionary<Guid, ProcedureRank> { [UnchosenVersion] = FirmLevel };

        var result = Resolve([row], chosen);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.Rank.Should().Be(FirmLevel);
    }

    [Fact]
    public void ACardOfNoVersion_IsNeverUsed()
    {
        var row = Row(2m, timekeeper: Tk) with { ProcedureVersionId = null };

        Resolve([row]).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void WhenNoVersionIsChosen_NothingIsUsed()
    {
        Resolve([Row(2m, timekeeper: Tk)], chosen: Chosen()).Status.Should().Be(RateResolutionStatus.NotFound);
    }

    [Fact]
    public void WorkInTheWindowOfEachVersion_UsesThatVersionsCard()
    {
        // v1 and v2 of one Procedure share a rank; the resolver chose v1 for the earlier date and v2 for the later one.
        var v1 = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
        var v2 = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
        var rows = new[] { Row(300m, timekeeper: Tk, version: v1), Row(350m, timekeeper: Tk, version: v2) };

        var inV1 = Resolve(rows, new Dictionary<Guid, ProcedureRank> { [v1] = FirmLevel }, date: new DateOnly(2025, 12, 31));
        var inV2 = Resolve(rows, new Dictionary<Guid, ProcedureRank> { [v2] = FirmLevel }, date: new DateOnly(2026, 1, 1));
        var pinnedToV1 = Resolve(rows, new Dictionary<Guid, ProcedureRank> { [v1] = ManualD0 }, date: new DateOnly(2026, 1, 1));

        inV1.ApprovedRate.Should().Be(300m);
        inV2.ApprovedRate.Should().Be(350m);
        pinnedToV1.ApprovedRate.Should().Be(300m, "a record pinned to v1 keeps v1's card for v2-window work");
    }

    // ── Precedence: the rank, then the basis ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Precedence_ManualD0_BeatsManualD1_BeatsFirm_BeatsAllRecords()
    {
        var rows = new[]
        {
            Row(4m, timekeeper: Tk, rank: AllRecords),
            Row(3m, timekeeper: Tk, rank: FirmLevel),
            Row(2m, timekeeper: Tk, rank: ManualD1),
            Row(1m, timekeeper: Tk, rank: ManualD0),
        };
        var all = Chosen(ManualD0, ManualD1, FirmLevel, AllRecords);

        Resolve(rows, all).ApprovedRate.Should().Be(1m);
        Resolve(rows, all).Rank.Should().Be(ManualD0);
        Resolve(rows, Chosen(ManualD1, FirmLevel, AllRecords)).ApprovedRate.Should().Be(2m);
        Resolve(rows, Chosen(FirmLevel, AllRecords)).ApprovedRate.Should().Be(3m);
        Resolve(rows, Chosen(AllRecords)).ApprovedRate.Should().Be(4m);
    }

    [Fact]
    public void Precedence_AFirmSpecificProcedure_BeatsAGenericOneOfTheSameLevelAndDistance()
    {
        var rows = new[] { Row(4m, timekeeper: Tk, rank: AllRecords), Row(5m, timekeeper: Tk, rank: AllRecordsFirmSpecific) };

        var result = Resolve(rows, Chosen(AllRecords, AllRecordsFirmSpecific));

        result.ApprovedRate.Should().Be(5m);
        result.Rank.Should().Be(AllRecordsFirmSpecific);
    }

    [Fact]
    public void Precedence_TheRankDecidesBeforeTheBasis_AHigherRankedClassificationRateBeatsALowerRankedTimekeeperRate()
    {
        var result = Resolve(
            [Row(3m, timekeeper: Tk, rank: AllRecords), Row(2m, classification: Partner, rank: ManualD0), Row(4m, classification: Partner, rank: AllRecords)],
            Chosen(ManualD0, AllRecords));

        result.ApprovedRate.Should().Be(2m);
        result.Rank.Should().Be(ManualD0);
        result.Basis.Should().Be(RateBasis.Classification);
    }

    [Fact]
    public void Precedence_WithinOneRank_ATimekeeperRateBeatsAClassificationRate()
    {
        var result = Resolve([Row(4m, classification: Partner), Row(3m, timekeeper: Tk)]);

        result.ApprovedRate.Should().Be(3m);
        result.Basis.Should().Be(RateBasis.Timekeeper);
    }

    [Fact]
    public void Precedence_FallsThroughToTheLowerRank_WhenTheHigherRankedCardHasNoRateForThisPerson()
    {
        var result = Resolve(
            [Row(2m, timekeeper: OtherTk, rank: ManualD0), Row(4m, classification: Partner, rank: FirmLevel)],
            Chosen(ManualD0, FirmLevel));

        result.ApprovedRate.Should().Be(4m);
        result.Rank.Should().Be(FirmLevel);
    }

    [Fact]
    public void Precedence_DoesNotDependOnTheOrderOfTheRows()
    {
        var rows = new[]
        {
            Row(1m, timekeeper: Tk, rank: ManualD0),
            Row(2m, classification: Partner, rank: ManualD0),
            Row(3m, timekeeper: Tk, rank: FirmLevel),
            Row(4m, classification: Partner, rank: AllRecords),
        };
        var chosen = Chosen(ManualD0, FirmLevel, AllRecords);

        var expected = Resolve(rows, chosen);
        Resolve(rows.Reverse().ToArray(), chosen).Should().BeEquivalentTo(expected);
        Resolve(rows.OrderBy(r => r.RateId).ToArray(), chosen).Should().BeEquivalentTo(expected);
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
    public void TwoRatesOfDifferentAmounts_AtTheDecidingTier_AreAmbiguous_AndNoRateIsPicked()
    {
        var result = Resolve([Row(400m, timekeeper: Tk), Row(450m, timekeeper: Tk)]);

        result.Status.Should().Be(RateResolutionStatus.Ambiguous);
        result.ApprovedRate.Should().BeNull();
        result.RateId.Should().BeNull();
    }

    [Fact]
    public void TwoCardsOfEqualRank_WithDifferentAmounts_AreAmbiguous_EvenWhenTheyAreOfDifferentProcedures()
    {
        // Two Procedures of the same rank (both All records, say): their versions differ, their rank is equal.
        var other = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000f2");
        var chosen = new Dictionary<Guid, ProcedureRank> { [VersionOf(AllRecords)] = AllRecords, [other] = AllRecords };

        var result = Resolve([Row(400m, timekeeper: Tk), Row(450m, timekeeper: Tk, version: other)], chosen);

        result.Status.Should().Be(RateResolutionStatus.Ambiguous);
    }

    [Fact]
    public void TwoRatesOfTheSameAmount_AtTheDecidingTier_AreOneRate()
    {
        var a = Row(400m, timekeeper: Tk);
        var b = Row(400m, timekeeper: Tk);

        var result = Resolve([b, a]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(400m);
    }

    [Fact]
    public void ADisagreement_BelowTheDecidingTier_DoesNotMatter()
    {
        var result = Resolve([Row(1m, timekeeper: Tk), Row(2m, classification: Partner), Row(3m, classification: Partner)]);

        result.Status.Should().Be(RateResolutionStatus.Found);
        result.ApprovedRate.Should().Be(1m);
    }

    [Fact]
    public void ADisagreement_AtALowerRank_DoesNotMatter_WhenTheHigherRankDecides()
    {
        var result = Resolve(
            [Row(1m, timekeeper: Tk, rank: ManualD0), Row(2m, timekeeper: Tk, rank: AllRecords), Row(3m, timekeeper: Tk, rank: AllRecordsFirmSpecific)],
            Chosen(ManualD0, AllRecords, AllRecordsFirmSpecific));

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
        var result = RateCardResolver.Resolve([Row(2m, timekeeper: Tk)], Firm, Chosen(AllRecords), Matched(), lineDate: null);

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
        RateRow[] rows = [Row(1m, timekeeper: Tk, rank: ManualD0), Row(1m, timekeeper: Tk, rank: ManualD0), Row(2m, classification: Partner)];
        var chosen = Chosen(ManualD0, AllRecords);

        Resolve(rows, chosen).Should().BeEquivalentTo(Resolve(rows, chosen));
    }

    // ── The rank as text ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ProcedureLevel.ManualOwn, 0, false, "ManualOwn/d0")]
    [InlineData(ProcedureLevel.ManualParent, 1, true, "ManualParent/d1/firmSpecific")]
    [InlineData(ProcedureLevel.Firm, 0, true, "Firm/d0/firmSpecific")]
    [InlineData(ProcedureLevel.AllRecords, 0, false, "AllRecords/d0")]
    public void TheRankLabel_IsBuiltFromLevelDistanceAndFirmSpecificOnly(ProcedureLevel level, int distance, bool firmSpecific, string expected)
    {
        RateRankLabel.Of(new ProcedureRank(level, distance, firmSpecific)).Should().Be(expected);
    }
}
