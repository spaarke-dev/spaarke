namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>The Published status of <c>sprk_ratecard</c> (task 017 notes section 4).</summary>
public static class RateCardStatus
{
    /// <summary>statuscode "Published" (statecode 0). Draft is 1, Inactive is 2.</summary>
    public const int Published = 659490000;
}

/// <summary>One <c>sprk_ratecardrate</c> row with the facts of its card that decide whether it applies.</summary>
/// <param name="CardFirmId">The card's firm.</param>
/// <param name="CardPublished">The card's status is Published.</param>
/// <param name="CardScopes">The core records the card is scoped to: none = a firm-wide card, one = a scoped card. More than one violates the writer's rule (task 078) and the card is not used.</param>
/// <param name="RateActive">The rate row is active (<c>statecode = 0</c>).</param>
/// <param name="TimekeeperId">The timekeeper the rate is for; <c>null</c> = the rate is for a classification.</param>
/// <param name="Classification">The <c>sprk_timekeeperrole</c> option value of a classification rate.</param>
public sealed record RateRow(
    Guid RateId,
    Guid CardId,
    Guid? CardFirmId,
    bool CardPublished,
    IReadOnlyList<CoreRecordReference> CardScopes,
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
/// The rate in force for a fee line (FR-23, FR-08): the approved rate of the Published rate card that is in force on the line date.
/// Pure and deterministic; the rows come from one read per invoice.
/// </summary>
/// <remarks>
/// <para><b>A row applies</b> when its rate is active, its card is Published, the card is of the invoice's firm, the line date is within
/// both the card's and the rate's effective dates (both ends inclusive; an empty end is open), the card is firm-wide or scoped to the
/// invoice's core record (a card scoped to another core record never applies), and the rate is for the matched timekeeper, or for the
/// timekeeper's classification when it names no timekeeper.</para>
/// <para><b>Precedence</b> (assumption recorded in the task notes): the card scope decides first, the basis second.
/// (1) core-record card, timekeeper rate; (2) core-record card, classification rate; (3) firm card, timekeeper rate; (4) firm card,
/// classification rate. A matter-specific card therefore overrides the firm's general card even for a timekeeper the general card names,
/// and within one card an individual's rate beats the classification's. The first tier that has any rate decides; lower tiers are not
/// consulted.</para>
/// <para><b>Disagreement is not resolved by guessing.</b> If the deciding tier holds rates of different amounts (two Published cards in
/// force on the same date), the result is <see cref="RateResolutionStatus.Ambiguous"/> and no rate is used. Equal amounts are one rate.</para>
/// <para><b>No rate is not an error.</b> An unmatched timekeeper (no classification to look up), a line without a date, or no card in
/// force gives <see cref="RateResolutionStatus.NotFound"/>, which the caller turns into a reference-missing signal.</para>
/// </remarks>
public static class RateCardResolver
{
    public static RateResolution Resolve(
        IReadOnlyList<RateRow> rows,
        Guid firmId,
        CoreRecordReference? coreRecord,
        TimekeeperResolution timekeeper,
        DateOnly? lineDate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(timekeeper);

        if (timekeeper.TimekeeperId is not { } timekeeperId)
        {
            return NotFound(RateNotFoundReason.TimekeeperUnmatched);
        }

        if (lineDate is not { } date)
        {
            return NotFound(RateNotFoundReason.LineDateMissing);
        }

        var candidates = new List<(RateRow Row, RateCardScope Scope, RateBasis Basis)>();
        foreach (var row in rows)
        {
            if (!Applies(row, firmId, coreRecord, date, out var scope)
                || !TryBasis(row, timekeeperId, timekeeper.Classification, out var basis))
            {
                continue;
            }

            candidates.Add((row, scope, basis));
        }

        if (candidates.Count == 0)
        {
            return NotFound(RateNotFoundReason.NoRateInForce);
        }

        // Tier order: core-record card before firm card, then timekeeper before classification.
        var best = candidates.Min(c => Tier(c.Scope, c.Basis));
        var winners = candidates.Where(c => Tier(c.Scope, c.Basis) == best).ToList();

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
            Scope = pick.Scope,
            Basis = pick.Basis,
        };
    }

    private static int Tier(RateCardScope scope, RateBasis basis) =>
        (scope == RateCardScope.CoreRecord ? 0 : 2) + (basis == RateBasis.Timekeeper ? 0 : 1);

    private static RateResolution NotFound(RateNotFoundReason reason) =>
        new() { Status = RateResolutionStatus.NotFound, Reason = reason };

    private static bool Applies(RateRow row, Guid firmId, CoreRecordReference? coreRecord, DateOnly date, out RateCardScope scope)
    {
        scope = RateCardScope.Firm;

        if (!row.RateActive
            || !row.CardPublished
            || row.CardFirmId != firmId
            || row.ApprovedRate is null
            || !InForce(date, row.CardEffectiveFrom, row.CardEffectiveTo)
            || !InForce(date, row.RateEffectiveFrom, row.RateEffectiveTo))
        {
            return false;
        }

        switch (row.CardScopes.Count)
        {
            case 0:
                scope = RateCardScope.Firm;
                return true;
            case 1 when coreRecord is not null && row.CardScopes[0] == coreRecord:
                scope = RateCardScope.CoreRecord;
                return true;
            default:
                // Scoped to another core record, or to several (which the writer forbids): not for this invoice.
                return false;
        }
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
