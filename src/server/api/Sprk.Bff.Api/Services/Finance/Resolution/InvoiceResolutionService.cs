using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>
/// <see cref="IInvoiceResolutionService"/> over Dataverse: reads the firm's roster, the Procedure Versions chosen for the invoice (082) and the
/// rate rows of their cards in force, once per invoice, then runs the pure resolvers (<see cref="TimekeeperMatcher"/>,
/// <see cref="RateCardResolver"/>, <see cref="ArithmeticChecker"/>) and the <see cref="CombinedConfidenceCalculator"/>.
/// </summary>
/// <remarks>
/// <para><b>Reads.</b> Per invoice: the firm's <c>sprk_timekeeper</c> rows (all statuses, so an unapproved person can be told from an
/// unknown one); the Procedure rows <see cref="ProcedureResolver"/> reads once (082; <see cref="ProcedureResolution.RulesOn"/> then runs in
/// memory for each distinct line work date); and the <c>sprk_ratecardrate</c> rows of the Published cards of the Procedure Versions that
/// resolution chose, in force across the invoice's line dates, joined to their card (085, model 7.1). A read is skipped when no line needs
/// it (no timekeeper names / no matched fee line with a date / no version chosen). Line count never changes the number of reads. Firm,
/// status and version conditions are in the query to keep the read small; the resolver applies them again on the rows, so a query that
/// returned too much cannot change a result.</para>
/// <para><b>Failure.</b> A Dataverse failure propagates (a resolution made from half-read reference data would be wrong either way), and so
/// does a <see cref="ProcedureResolutionFaultException"/>: no rate is used on a partial set. Only the AI-assisted name match degrades, to
/// "unmatched".</para>
/// <para><b>Logging</b> carries counts only, never names or amounts (ADR-015).</para>
/// </remarks>
public sealed class InvoiceResolutionService : IInvoiceResolutionService
{
    internal const string TimekeeperEntity = "sprk_timekeeper";
    internal const string RateEntity = "sprk_ratecardrate";
    internal const string RateCardEntity = "sprk_ratecard";
    internal const string CardAlias = "card";

    private const int PageSize = 5000;

    /// <summary><c>sprk_ratecard.sprk_procedureversion</c>: the Procedure Version a card belongs to (D-120: cards hang from it, not from a core record).</summary>
    internal const string CardProcedureVersionColumn = "sprk_procedureversion";

    private readonly IGenericEntityService _entities;
    private readonly IEbillingSettingsReader _settings;
    private readonly TimekeeperMatcher _matcher;
    private readonly ILogger<InvoiceResolutionService> _logger;

    public InvoiceResolutionService(
        IGenericEntityService entities,
        IEbillingSettingsReader settings,
        TimekeeperMatcher matcher,
        ILogger<InvoiceResolutionService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InvoiceResolutionResult> ResolveAsync(InvoiceResolutionInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Header);
        ArgumentNullException.ThrowIfNull(input.Lines);
        // The empty id is never "no firm" (task 095c-3: null is, and reads no roster or rate card).
        if (input.FirmId == Guid.Empty)
        {
            throw new ArgumentException("The submission context names the empty firm id; no roster or rate card can be chosen.", nameof(input));
        }

        var settings = await _settings.GetAsync(ct);
        var arithmetic = ArithmeticChecker.Check(input);

        // ── Timekeepers (fee lines): one roster read, then the matcher. ─────────────────────────────────────
        var feeLines = input.Lines
            .Select((line, index) => (Line: line, Index: index))
            .Where(x => x.Line.LineType == InvoiceLineType.Fee)
            .ToList();

        var timekeepers = new Dictionary<int, TimekeeperResolution>();
        var rosterRows = 0;
        if (feeLines.Count > 0)
        {
            var requests = feeLines
                .Select(x => new TimekeeperMatchRequest(x.Line.TimekeeperAsBilled.Value, x.Line.Date.Value))
                .ToList();

            if (input.FirmId is { } firmId)
            {
                var needsRoster = requests.Any(r => !string.IsNullOrWhiteSpace(r.BilledName) && r.LineDate.HasValue);
                var roster = needsRoster ? await ReadRosterAsync(firmId, ct) : [];
                rosterRows = roster.Count;

                var matches = await _matcher.MatchAsync(
                    firmId, roster, input.TimekeeperSummaryNames, requests, settings.ReliabilityThreshold, ct);
                for (var i = 0; i < feeLines.Count; i++)
                {
                    timekeepers[feeLines[i].Index] = matches[i];
                }
            }
            else
            {
                // Task 095c-3: no firm, so no roster to match against (and no AI-assisted match): the match is not established.
                foreach (var (_, index) in feeLines)
                {
                    timekeepers[index] = new TimekeeperResolution
                    {
                        Stage = TimekeeperMatchStage.None, MatchConfidence = 0m, Reason = TimekeeperUnmatchedReason.None,
                    };
                }
            }
        }

        // ── Rates (matched fee lines with a date): one Procedure resolution, then one rate read. ────────────
        var dated = feeLines
            .Where(x => timekeepers[x.Index].IsMatched && x.Line.Date.Value.HasValue)
            .Select(x => x.Line.Date.Value!.Value)
            .ToList();
        IReadOnlyList<RateRow> rates = [];
        var chosenByDate = new Dictionary<DateOnly, IReadOnlyDictionary<Guid, ProcedureRank>>();
        if (dated.Count > 0 && input.FirmId is { } rateFirm)
        {
            // 085: the versions come from 082's resolver, once per invoice; a fault propagates (the job retries), so no rate is ever
            // judged on a partial set. RulesOn runs in memory, once per distinct work date.
            var procedures = await ProcedureResolver.ReadAsync(_entities, _logger, input.CoreRecord, rateFirm, ct);
            foreach (var date in dated.Distinct())
            {
                chosenByDate[date] = procedures.RulesOn(date).Procedures.ToDictionary(p => p.ProcedureVersionId, p => p.Rank);
            }

            var versionIds = chosenByDate.Values.SelectMany(v => v.Keys).Distinct().ToList();
            rates = versionIds.Count > 0 ? await ReadRatesAsync(rateFirm, versionIds, dated.Min(), dated.Max(), ct) : [];
        }

        // ── Assemble: per line, per field. ──────────────────────────────────────────────────────────────────
        var lines = new List<ResolvedInvoiceLine>(input.Lines.Count);
        var findingInputs = new List<ApprovedTimekeeperFindingInput>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            TimekeeperResolution? timekeeper = null;
            var rate = new RateResolution { Status = RateResolutionStatus.NotApplicable };

            if (line.LineType == InvoiceLineType.Fee)
            {
                timekeeper = timekeepers[i];
                rate = input.FirmId is { } lineFirm
                    ? RateCardResolver.Resolve(rates, lineFirm, ChosenOn(chosenByDate, line.Date.Value), timekeeper, line.Date.Value)
                    : new RateResolution { Status = RateResolutionStatus.NotFound, Reason = RateNotFoundReason.TimekeeperUnmatched };
                if (!timekeeper.IsMatched)
                {
                    findingInputs.Add(new ApprovedTimekeeperFindingInput(
                        i, line.TimekeeperAsBilled.Value, line.Date.Value, timekeeper.Reason));
                }
            }

            lines.Add(new ResolvedInvoiceLine
            {
                LineIndex = i,
                LineType = line.LineType,
                Timekeeper = timekeeper,
                Rate = rate,
                Fields = LineFields(i, line, timekeeper, rate, arithmetic, settings),
            });
        }

        _logger.LogInformation(
            "Invoice resolution: {Lines} lines, {Matched} of {Fee} fee lines matched to the roster, {RosterRows} roster rows and {RateRows} rate rows read, {Failed} arithmetic checks failed",
            input.Lines.Count,
            timekeepers.Values.Count(t => t.IsMatched),
            feeLines.Count,
            rosterRows,
            rates.Count,
            arithmetic.Checks.Count(c => !c.Passed));

        return new InvoiceResolutionResult
        {
            HeaderFields = HeaderFields(input.Header, arithmetic, settings),
            Lines = lines,
            ArithmeticChecks = arithmetic.Checks,
            ApprovedTimekeeperFindingInputs = findingInputs,
        };
    }

    // ── Field confidence ─────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, FieldConfidence> HeaderFields(
        InvoiceResolutionHeader header, ArithmeticReport arithmetic, EbillingSettings settings)
    {
        var fields = new Dictionary<string, FieldConfidence>(StringComparer.Ordinal);

        void Add(string name, FieldReading reading) =>
            fields[name] = Combine(reading, arithmetic.OutcomeOf(null, name), ReferenceOutcome.NotApplicable, 1m, settings);

        Add(ResolutionFieldNames.FeesTotal, header.FeesTotal.Reading);
        Add(ResolutionFieldNames.ExpensesTotal, header.ExpensesTotal.Reading);
        Add(ResolutionFieldNames.AdjustmentsTotal, header.AdjustmentsTotal.Reading);
        Add(ResolutionFieldNames.TotalDue, header.TotalDue.Reading);
        AddOthers(fields, header.OtherFields, settings);
        return fields;
    }

    private static Dictionary<string, FieldConfidence> LineFields(
        int index,
        InvoiceResolutionLine line,
        TimekeeperResolution? timekeeper,
        RateResolution rate,
        ArithmeticReport arithmetic,
        EbillingSettings settings)
    {
        var fields = new Dictionary<string, FieldConfidence>(StringComparer.Ordinal);

        void Add(string name, FieldReading reading, ReferenceOutcome reference = ReferenceOutcome.NotApplicable, decimal strength = 1m) =>
            fields[name] = Combine(reading, arithmetic.OutcomeOf(index, name), reference, strength, settings);

        Add(ResolutionFieldNames.Date, line.Date.Reading);
        if (line.LineType == InvoiceLineType.Fee)
        {
            // The roster match is the reference for the name; the rate card (reached through that match) is the reference for the rate,
            // so a rate found through an AI-assisted name is only as strong as that name match.
            var matched = timekeeper is { IsMatched: true };
            var strength = timekeeper?.MatchConfidence ?? 0m;
            Add(
                ResolutionFieldNames.TimekeeperAsBilled,
                line.TimekeeperAsBilled.Reading,
                matched ? ReferenceOutcome.Matched : ReferenceOutcome.Missing,
                strength);
            Add(ResolutionFieldNames.Hours, line.Hours.Reading);
            Add(
                ResolutionFieldNames.Rate,
                line.Rate.Reading,
                rate.Status == RateResolutionStatus.Found ? ReferenceOutcome.Matched : ReferenceOutcome.Missing,
                strength);
        }
        else
        {
            Add(ResolutionFieldNames.Quantity, line.Quantity.Reading);
            Add(ResolutionFieldNames.UnitCost, line.UnitCost.Reading);
        }

        Add(ResolutionFieldNames.Amount, line.Amount.Reading);
        AddOthers(fields, line.OtherFields, settings);
        return fields;
    }

    private static void AddOthers(
        Dictionary<string, FieldConfidence> fields, IReadOnlyDictionary<string, FieldReading>? others, EbillingSettings settings)
    {
        if (others is null)
        {
            return;
        }

        foreach (var (name, reading) in others)
        {
            // A named field above wins over an OtherFields entry of the same name.
            if (!fields.ContainsKey(name))
            {
                fields[name] = Combine(reading, ArithmeticOutcome.NotApplicable, ReferenceOutcome.NotApplicable, 1m, settings);
            }
        }
    }

    private static FieldConfidence Combine(
        FieldReading reading, ArithmeticOutcome arithmetic, ReferenceOutcome reference, decimal strength, EbillingSettings settings) =>
        CombinedConfidenceCalculator.Calculate(
            new FieldSignals(reading.AnalyzerConfidence, reading.SelfReportConfidence, arithmetic, reference, strength), settings);

    // ── Reference-data reads ─────────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<RosterEntry>> ReadRosterAsync(Guid firmId, CancellationToken ct)
    {
        var query = new QueryExpression(TimekeeperEntity)
        {
            ColumnSet = new ColumnSet(
                "sprk_name", "sprk_firm", "sprk_effectivefrom", "sprk_effectiveto", "sprk_classification", "statecode", "statuscode"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition("sprk_firm", ConditionOperator.Equal, firmId);
        query.AddOrder("sprk_timekeeperid", OrderType.Ascending);

        var rows = new List<RosterEntry>();
        await foreach (var row in PagesAsync(query, ct))
        {
            rows.Add(new RosterEntry(
                row.Id,
                row.GetAttributeValue<EntityReference>("sprk_firm")?.Id,
                row.GetAttributeValue<string>("sprk_name") ?? string.Empty,
                row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0,
                row.GetAttributeValue<OptionSetValue>("statuscode")?.Value,
                ToDate(row.GetAttributeValue<DateTime?>("sprk_effectivefrom")),
                ToDate(row.GetAttributeValue<DateTime?>("sprk_effectiveto")),
                row.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value));
        }

        return rows;
    }

    private static IReadOnlyDictionary<Guid, ProcedureRank> ChosenOn(
        Dictionary<DateOnly, IReadOnlyDictionary<Guid, ProcedureRank>> chosenByDate, DateOnly? date) =>
        date is { } d && chosenByDate.TryGetValue(d, out var chosen) ? chosen : new Dictionary<Guid, ProcedureRank>();

    private async Task<IReadOnlyList<RateRow>> ReadRatesAsync(
        Guid firmId, IReadOnlyCollection<Guid> procedureVersionIds, DateOnly earliest, DateOnly latest, CancellationToken ct)
    {
        var query = BuildRateQuery(firmId, procedureVersionIds, earliest, latest);

        var rows = new List<RateRow>();
        await foreach (var row in PagesAsync(query, ct))
        {
            rows.Add(new RateRow(
                RateId: row.Id,
                CardId: row.GetAttributeValue<EntityReference>("sprk_ratecard")?.Id ?? Guid.Empty,
                CardFirmId: (Aliased(row, "sprk_firm") as EntityReference)?.Id,
                CardPublished: (Aliased(row, "statuscode") as OptionSetValue)?.Value == RateCardStatus.Published
                    && (Aliased(row, "statecode") as OptionSetValue)?.Value == 0,
                ProcedureVersionId: (Aliased(row, CardProcedureVersionColumn) as EntityReference)?.Id,
                CardEffectiveFrom: ToDate(Aliased(row, "sprk_effectivefrom") as DateTime?),
                CardEffectiveTo: ToDate(Aliased(row, "sprk_effectiveto") as DateTime?),
                RateActive: row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0,
                RateEffectiveFrom: ToDate(row.GetAttributeValue<DateTime?>("sprk_effectivefrom")),
                RateEffectiveTo: ToDate(row.GetAttributeValue<DateTime?>("sprk_effectiveto")),
                TimekeeperId: row.GetAttributeValue<EntityReference>("sprk_timekeeper")?.Id,
                Classification: row.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value,
                ApprovedRate: row.GetAttributeValue<Money>("sprk_approvedrate")?.Value,
                RackRate: row.GetAttributeValue<Money>("sprk_rackrate")?.Value));
        }

        return rows;
    }

    /// <summary>
    /// The active rates of the firm's Published cards whose Procedure Version is one of <paramref name="procedureVersionIds"/> (the versions
    /// 082 chose; model 7.1), in force across [earliest, latest]. No core-record lookup of the card is read: a card belongs to a Procedure
    /// Version, not to a core record (D-120).
    /// </summary>
    internal static QueryExpression BuildRateQuery(
        Guid firmId, IReadOnlyCollection<Guid> procedureVersionIds, DateOnly earliest, DateOnly latest)
    {
        ArgumentNullException.ThrowIfNull(procedureVersionIds);
        var query = new QueryExpression(RateEntity)
        {
            ColumnSet = new ColumnSet(
                "sprk_ratecard", "sprk_timekeeper", "sprk_classification", "sprk_approvedrate", "sprk_rackrate",
                "sprk_effectivefrom", "sprk_effectiveto", "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddFilter(Overlapping("sprk_effectivefrom", "sprk_effectiveto", earliest, latest));
        query.AddOrder("sprk_ratecardrateid", OrderType.Ascending);

        var card = query.AddLink(RateCardEntity, "sprk_ratecard", "sprk_ratecardid", JoinOperator.Inner);
        card.EntityAlias = CardAlias;
        card.Columns = new ColumnSet(
            CardProcedureVersionColumn, "sprk_firm", "sprk_effectivefrom", "sprk_effectiveto", "statecode", "statuscode");
        card.LinkCriteria = new FilterExpression(LogicalOperator.And);
        card.LinkCriteria.AddCondition("sprk_firm", ConditionOperator.Equal, firmId);
        card.LinkCriteria.AddCondition("statuscode", ConditionOperator.Equal, RateCardStatus.Published);
        card.LinkCriteria.AddCondition(CardProcedureVersionColumn, ConditionOperator.In, procedureVersionIds.Cast<object>().ToArray());
        card.LinkCriteria.AddFilter(Overlapping("sprk_effectivefrom", "sprk_effectiveto", earliest, latest));
        return query;
    }

    /// <summary>Rows whose [from, to] (either end may be empty) overlaps [earliest, latest].</summary>
    private static FilterExpression Overlapping(string fromColumn, string toColumn, DateOnly earliest, DateOnly latest)
    {
        var started = new FilterExpression(LogicalOperator.Or);
        started.AddCondition(fromColumn, ConditionOperator.Null);
        started.AddCondition(fromColumn, ConditionOperator.OnOrBefore, latest.ToDateTime(TimeOnly.MinValue));

        var notEnded = new FilterExpression(LogicalOperator.Or);
        notEnded.AddCondition(toColumn, ConditionOperator.Null);
        notEnded.AddCondition(toColumn, ConditionOperator.OnOrAfter, earliest.ToDateTime(TimeOnly.MinValue));

        var overlap = new FilterExpression(LogicalOperator.And);
        overlap.AddFilter(started);
        overlap.AddFilter(notEnded);
        return overlap;
    }

    private async IAsyncEnumerable<Entity> PagesAsync(
        QueryExpression query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _entities.RetrieveMultipleAsync(query, ct);
            foreach (var entity in page.Entities)
            {
                yield return entity;
            }

            if (!page.MoreRecords)
            {
                yield break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>The value of a linked card's column (<c>card.&lt;column&gt;</c>), or null when it is empty or absent.</summary>
    private static object? Aliased(Entity row, string column) =>
        row.GetAttributeValue<AliasedValue>($"{CardAlias}.{column}")?.Value;

    private static DateOnly? ToDate(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;
}
