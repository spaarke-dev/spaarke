using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>The Published status of <c>sprk_ratecard</c> (task 017 notes section 4).</summary>
public static class RateCardStatus
{
    /// <summary>statuscode "Published" (statecode 0). Draft is 1, Inactive is 2.</summary>
    public const int Published = 659490000;
}

/// <summary>One <c>sprk_ratecardrate</c> row with the facts of its card that decide whether it applies.</summary>
/// <param name="CardFirmId">The card's firm.</param>
/// <param name="CardPublished">The card's status is Published and the card is active.</param>
/// <param name="ProcedureVersionId">The card's Procedure Version (<c>sprk_ratecard.sprk_procedureversion</c>); <c>null</c> = the card belongs to no version and is never used.</param>
/// <param name="RateActive">The rate row is active (<c>statecode = 0</c>).</param>
/// <param name="TimekeeperId">The timekeeper the rate is for; <c>null</c> = the rate is for a classification.</param>
/// <param name="Classification">The <c>sprk_timekeeperrole</c> option value of a classification rate.</param>
public sealed record RateRow(
    Guid RateId,
    Guid CardId,
    Guid? CardFirmId,
    bool CardPublished,
    Guid? ProcedureVersionId,
    DateOnly? CardEffectiveFrom,
    DateOnly? CardEffectiveTo,
    bool RateActive,
    DateOnly? RateEffectiveFrom,
    DateOnly? RateEffectiveTo,
    Guid? TimekeeperId,
    int? Classification,
    decimal? ApprovedRate,
    decimal? RackRate);

/// <summary>
/// The rate in force for a fee line (FR-23, FR-08; ebilling-r1 085, model 7.1 and 7.3): the approved rate of the Published rate card of the
/// Procedure Version that Procedure resolution (082) chose for the line's work date. Pure and deterministic; the rows come from one read per
/// invoice and the chosen versions from one <see cref="ProcedureResolution"/> per invoice.
/// </summary>
/// <remarks>
/// <para><b>A row applies</b> when its rate is active, its card is Published and active, the card's Procedure Version is one of
/// <c>chosenVersions</c> (the versions 082 chose for THIS line's work date, or the version a record is pinned to), the card is of the
/// invoice's firm, the line date is within both the card's and the rate's effective dates (both ends inclusive; an empty end is open),
/// and the rate is for the matched timekeeper, or for the timekeeper's classification when it names no timekeeper. A card of a version that
/// was not chosen never applies, whatever its dates.</para>
/// <para><b>Precedence.</b> The card's rank decides first, by the resolver's key (Level, -Distance, FirmSpecific): Manual d0 &gt; Manual d1
/// &gt; Firm &gt; All records. The basis decides second (timekeeper before classification). The first tier that has any rate decides; lower
/// tiers are not consulted.</para>
/// <para><b>Disagreement is not resolved by guessing.</b> If the deciding tier holds rates of different amounts (two Published cards of
/// equal rank in force on the same date), the result is <see cref="RateResolutionStatus.Ambiguous"/> and no rate is used. Equal amounts are
/// one rate.</para>
/// <para><b>No rate is not an error.</b> An unmatched timekeeper (no classification to look up), a line without a date, or no card in
/// force gives <see cref="RateResolutionStatus.NotFound"/>, which the caller turns into a reference-missing signal.</para>
/// </remarks>
public static class RateCardResolver
{
    public static RateResolution Resolve(
        IReadOnlyList<RateRow> rows,
        Guid firmId,
        IReadOnlyDictionary<Guid, ProcedureRank> chosenVersions,
        TimekeeperResolution timekeeper,
        DateOnly? lineDate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(chosenVersions);
        ArgumentNullException.ThrowIfNull(timekeeper);

        if (timekeeper.TimekeeperId is not { } timekeeperId)
        {
            return NotFound(RateNotFoundReason.TimekeeperUnmatched);
        }

        if (lineDate is not { } date)
        {
            return NotFound(RateNotFoundReason.LineDateMissing);
        }

        var candidates = new List<(RateRow Row, ProcedureRank Rank, RateBasis Basis)>();
        foreach (var row in rows)
        {
            if (!Applies(row, firmId, chosenVersions, date, out var rank)
                || !TryBasis(row, timekeeperId, timekeeper.Classification, out var basis))
            {
                continue;
            }

            candidates.Add((row, rank, basis));
        }

        if (candidates.Count == 0)
        {
            return NotFound(RateNotFoundReason.NoRateInForce);
        }

        // Tier order: the higher-ranked card first, then timekeeper before classification.
        var best = candidates.OrderByDescending(c => c.Rank).ThenBy(c => c.Basis).First();
        var winners = candidates.Where(c => c.Rank == best.Rank && c.Basis == best.Basis).ToList();

        if (winners.Select(w => w.Row.ApprovedRate).Distinct().Count() > 1)
        {
            return new RateResolution { Status = RateResolutionStatus.Ambiguous };
        }

        var pick = winners.OrderBy(w => w.Row.CardId).ThenBy(w => w.Row.RateId).First();
        return new RateResolution
        {
            Status = RateResolutionStatus.Found,
            ApprovedRate = pick.Row.ApprovedRate,
            RackRate = pick.Row.RackRate,
            RateCardId = pick.Row.CardId,
            RateId = pick.Row.RateId,
            Rank = pick.Rank,
            Basis = pick.Basis,
        };
    }

    private static RateResolution NotFound(RateNotFoundReason reason) =>
        new() { Status = RateResolutionStatus.NotFound, Reason = reason };

    private static bool Applies(
        RateRow row, Guid firmId, IReadOnlyDictionary<Guid, ProcedureRank> chosenVersions, DateOnly date, out ProcedureRank rank)
    {
        rank = default;

        if (!row.RateActive
            || !row.CardPublished
            || row.CardFirmId != firmId
            || row.ApprovedRate is null
            || row.ProcedureVersionId is not { } versionId
            || !chosenVersions.TryGetValue(versionId, out rank)
            || !InForce(date, row.CardEffectiveFrom, row.CardEffectiveTo)
            || !InForce(date, row.RateEffectiveFrom, row.RateEffectiveTo))
        {
            return false;
        }

        return true;
    }

    private static bool TryBasis(RateRow row, Guid timekeeperId, int? classification, out RateBasis basis)
    {
        if (row.TimekeeperId is { } rowTimekeeper)
        {
            basis = RateBasis.Timekeeper;
            return rowTimekeeper == timekeeperId;
        }

        basis = RateBasis.Classification;
        return row.Classification is { } rowClassification && classification == rowClassification;
    }

    private static bool InForce(DateOnly date, DateOnly? from, DateOnly? to) =>
        (from is null || from <= date) && (to is null || date <= to);
}
