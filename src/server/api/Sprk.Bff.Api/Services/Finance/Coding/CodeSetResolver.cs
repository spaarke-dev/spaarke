using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Coding;

/// <summary>
/// Resolves the UTBMS code set of one invoice (ebilling-r1 task 047, FR-22) and loads its closed lists of allowed codes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order (FR-22).</b> The first step that yields a usable set wins:
/// (1) the set implied by the firm-supplied task codes on the invoice, (2) the default set mapped on the core record's matter
/// type, (3) the default set mapped on its practice area, (4) a set proposed by <see cref="ICodeSetSuggestionProvider"/>
/// (flagged for review), (5) the settings default (<c>sprk_ebillingsetting.sprk_defaultcodeset</c>, else "UTBMS Litigation").
/// A set is usable when it is active and has at least one Task code; a stale mapping (inactive or empty set) is skipped with a
/// warning rather than coding every line against nothing. No step yielding a set throws
/// <see cref="LineCodingException"/> (<see cref="LineCodingException.NoCodeSet"/>).
/// </para>
/// <para>
/// <b>Step 1 and the one-set rule.</b> The firm's codes select a set only when exactly one active set contains <i>every</i> distinct
/// task code the firm billed on the invoice. Zero sets (an unknown or mistyped code) or several (codes shared by more than one
/// set) is not decisive, and the resolver moves on to the mappings. Only <i>task</i> codes are used: the seeded activity and
/// expense codes sit in their own shared sets (task 018), so "all codes in one set" cannot hold across the three kinds.
/// </para>
/// <para>
/// <b>Activity and expense lists.</b> The resolved set's own Activity / Expense codes when it has any, else those of the shared
/// "UTBMS Activity" / "UTBMS Expense" sets. A missing shared set leaves the list empty (the lines of that kind get no code).
/// </para>
/// <para>
/// <b>Lifetime.</b> Scoped (its <see cref="ICodeSetSuggestionProvider"/> is scoped: task 048's provider calls the AI). Reads reference data per call and caches
/// nothing: one resolution per invoice is a handful of queries, and a stale code list must never outlive an admin edit (ADR-009
/// has no applicable cache here).
/// </para>
/// </remarks>
public sealed class CodeSetResolver
{
    internal const string CodeSetEntity = "sprk_billingcodeset";
    internal const string CodeEntity = "sprk_billingcode";
    internal const string MatterTypeEntity = "sprk_mattertype_ref";
    internal const string PracticeAreaEntity = "sprk_practicearea_ref";

    internal const string MatterTypeColumn = "sprk_mattertype";
    internal const string PracticeAreaColumn = "sprk_practicearea";
    internal const string DefaultCodeSetColumn = "sprk_defaultbillingcodeset";

    /// <summary>Settings default when <c>sprk_defaultcodeset</c> is empty (FR-22 step 5; seeded by task 018).</summary>
    internal const string FallbackDefaultSetName = "UTBMS Litigation";

    /// <summary>Shared standalone set holding the activity codes (task 018).</summary>
    internal const string ActivitySetName = "UTBMS Activity";

    /// <summary>Shared standalone set holding the expense codes (task 018).</summary>
    internal const string ExpenseSetName = "UTBMS Expense";

    private const int ActiveState = 0;
    private const int PageSize = 500;

    private readonly IGenericEntityService _entities;
    private readonly IEbillingSettingsReader _settings;
    private readonly ICodeSetSuggestionProvider _suggestions;
    private readonly ILogger<CodeSetResolver> _logger;

    public CodeSetResolver(
        IGenericEntityService entities,
        IEbillingSettingsReader settings,
        ICodeSetSuggestionProvider suggestions,
        ILogger<CodeSetResolver> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves the code set for the invoice whose lines are <paramref name="lines"/> and which belongs to
    /// <paramref name="coreRecord"/>.
    /// </summary>
    /// <exception cref="LineCodingException"><see cref="LineCodingException.NoCodeSet"/>: no step produced a usable set.</exception>
    public async Task<ResolvedCodeSet> ResolveAsync(
        CodingCoreRecord coreRecord,
        IReadOnlyList<InvoiceLineToCode> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coreRecord);
        ArgumentNullException.ThrowIfNull(lines);
        if (!CodingCoreRecord.IsSupported(coreRecord.EntityLogicalName))
        {
            throw new ArgumentException(
                $"'{coreRecord.EntityLogicalName}' is not a core record (sprk_matter, sprk_project, sprk_workassignment).",
                nameof(coreRecord));
        }

        // 1. The firm's own task codes.
        var firmCodes = DistinctFirmTaskCodes(lines);
        var firmSetId = await FindSetContainingAllAsync(firmCodes, cancellationToken).ConfigureAwait(false);
        if (firmSetId is { } fromFirm
            && await LoadAsync(fromFirm, CodeSetSource.FirmCodes, cancellationToken).ConfigureAwait(false) is { } firm)
        {
            return firm;
        }

        // 2 and 3. The default sets mapped on the record's matter type and practice area.
        var record = await _entities.RetrieveAsync(
            coreRecord.EntityLogicalName,
            coreRecord.Id,
            [MatterTypeColumn, PracticeAreaColumn],
            cancellationToken).ConfigureAwait(false);
        var matterTypeId = record.GetAttributeValue<EntityReference>(MatterTypeColumn)?.Id;
        var practiceAreaId = record.GetAttributeValue<EntityReference>(PracticeAreaColumn)?.Id;

        if (matterTypeId is { } matterType
            && await ReadMappedSetAsync(MatterTypeEntity, matterType, cancellationToken).ConfigureAwait(false) is { } matterSet
            && await LoadAsync(matterSet, CodeSetSource.MatterType, cancellationToken).ConfigureAwait(false) is { } byMatterType)
        {
            return byMatterType;
        }

        if (practiceAreaId is { } practiceArea
            && await ReadMappedSetAsync(PracticeAreaEntity, practiceArea, cancellationToken).ConfigureAwait(false) is { } areaSet
            && await LoadAsync(areaSet, CodeSetSource.PracticeArea, cancellationToken).ConfigureAwait(false) is { } byPracticeArea)
        {
            return byPracticeArea;
        }

        // 4. An AI proposal (task 048); the default provider proposes nothing.
        var suggested = await _suggestions.SuggestCodeSetAsync(
            new CodeSetSuggestionContext(coreRecord, matterTypeId, practiceAreaId, lines),
            cancellationToken).ConfigureAwait(false);
        if (suggested is { } suggestedId
            && await LoadAsync(suggestedId, CodeSetSource.AiSuggestion, cancellationToken).ConfigureAwait(false) is { } bySuggestion)
        {
            return bySuggestion;
        }

        // 5. The settings default.
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var defaultSetId = settings.DefaultCodeSetId
            ?? await FindSetByNameAsync(FallbackDefaultSetName, cancellationToken).ConfigureAwait(false);
        if (defaultSetId is { } fallbackId
            && await LoadAsync(fallbackId, CodeSetSource.SettingsDefault, cancellationToken).ConfigureAwait(false) is { } byDefault)
        {
            return byDefault;
        }

        _logger.LogError(
            "Line coding: no code set resolved for {Entity} {RecordId} (settings default {DefaultCodeSetId})",
            coreRecord.EntityLogicalName,
            coreRecord.Id,
            settings.DefaultCodeSetId);
        throw new LineCodingException(
            LineCodingException.NoCodeSet,
            "No UTBMS code set could be resolved: no firm codes, mapping or suggestion applied and the settings default code set is missing or has no task codes.");
    }

    // ── Step 1 ──────────────────────────────────────────────────────────────────

    private static List<string> DistinctFirmTaskCodes(IReadOnlyList<InvoiceLineToCode> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new List<string>();
        foreach (var line in lines)
        {
            var code = line.BilledTaskCode?.Trim();
            if (!string.IsNullOrEmpty(code) && seen.Add(code))
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>The one active set that contains every code as a Task code; <c>null</c> for none, or for more than one.</summary>
    private async Task<Guid?> FindSetContainingAllAsync(List<string> codes, CancellationToken ct)
    {
        if (codes.Count == 0)
        {
            return null;
        }

        var query = new QueryExpression(CodeEntity)
        {
            ColumnSet = new ColumnSet("sprk_code", "sprk_codeset"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_code", ConditionOperator.In, codes.Cast<object>().ToArray());
        query.Criteria.AddCondition("sprk_codetype", ConditionOperator.Equal, (int)BillingCodeKind.Task);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        query.AddOrder("sprk_code", OrderType.Ascending);

        var rows = await RetrieveAllAsync(query, ct).ConfigureAwait(false);

        var matched = new Dictionary<Guid, HashSet<string>>();
        foreach (var row in rows)
        {
            var setId = row.GetAttributeValue<EntityReference>("sprk_codeset")?.Id;
            var code = row.GetAttributeValue<string>("sprk_code")?.Trim();
            if (setId is null || string.IsNullOrEmpty(code))
            {
                continue;
            }

            if (!matched.TryGetValue(setId.Value, out var set))
            {
                matched[setId.Value] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            set.Add(code);
        }

        var containing = matched.Where(m => m.Value.Count == codes.Count).Select(m => m.Key).ToList();
        if (containing.Count == 1)
        {
            return containing[0];
        }

        _logger.LogInformation(
            "Line coding: {CodeCount} firm task codes did not select one code set ({SetCount} sets contain all of them); trying the mappings",
            codes.Count,
            containing.Count);
        return null;
    }

    // ── Steps 2 and 3 ───────────────────────────────────────────────────────────

    private async Task<Guid?> ReadMappedSetAsync(string entity, Guid id, CancellationToken ct)
    {
        var row = await _entities.RetrieveAsync(entity, id, [DefaultCodeSetColumn], ct).ConfigureAwait(false);
        return row.GetAttributeValue<EntityReference>(DefaultCodeSetColumn)?.Id;
    }

    // ── Loading a set ───────────────────────────────────────────────────────────

    /// <summary>The usable set (active, with Task codes) or <c>null</c> after a warning.</summary>
    private async Task<ResolvedCodeSet?> LoadAsync(Guid setId, CodeSetSource source, CancellationToken ct)
    {
        var setQuery = new QueryExpression(CodeSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_name"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        setQuery.Criteria.AddCondition("sprk_billingcodesetid", ConditionOperator.Equal, setId);
        setQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);

        var setRow = (await RetrieveAllAsync(setQuery, ct).ConfigureAwait(false)).FirstOrDefault();
        if (setRow is null)
        {
            _logger.LogWarning("Line coding: code set {CodeSetId} from {Source} is missing or inactive; skipping", setId, source);
            return null;
        }

        var codes = await LoadCodesAsync(setId, ct).ConfigureAwait(false);
        var task = ToOptions(codes, BillingCodeKind.Task);
        if (task.Count == 0)
        {
            _logger.LogWarning("Line coding: code set {CodeSetId} from {Source} has no active task codes; skipping", setId, source);
            return null;
        }

        var activity = ToOptions(codes, BillingCodeKind.Activity);
        if (activity.Count == 0)
        {
            activity = await LoadSharedAsync(ActivitySetName, BillingCodeKind.Activity, ct).ConfigureAwait(false);
        }

        var expense = ToOptions(codes, BillingCodeKind.Expense);
        if (expense.Count == 0)
        {
            expense = await LoadSharedAsync(ExpenseSetName, BillingCodeKind.Expense, ct).ConfigureAwait(false);
        }

        var name = setRow.GetAttributeValue<string>("sprk_name") ?? string.Empty;
        _logger.LogInformation(
            "Line coding: resolved code set {CodeSetId} via {Source} ({TaskCount} task, {ActivityCount} activity, {ExpenseCount} expense codes)",
            setId,
            source,
            task.Count,
            activity.Count,
            expense.Count);
        return new ResolvedCodeSet(setId, name, source, task, activity, expense);
    }

    private async Task<List<InvoiceCodeOption>> LoadSharedAsync(string setName, BillingCodeKind kind, CancellationToken ct)
    {
        var setId = await FindSetByNameAsync(setName, ct).ConfigureAwait(false);
        if (setId is null)
        {
            // A reference-data fault, not a normal condition: every line of that kind is sent without a list and comes back with no code.
            _logger.LogError("Line coding: shared code set '{SetName}' not found; no {Kind} codes will be offered", setName, kind);
            return [];
        }

        return ToOptions(await LoadCodesAsync(setId.Value, ct).ConfigureAwait(false), kind);
    }

    private async Task<Guid?> FindSetByNameAsync(string name, CancellationToken ct)
    {
        var query = new QueryExpression(CodeSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_name"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_name", ConditionOperator.Equal, name);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        query.AddOrder("createdon", OrderType.Ascending);

        var rows = await RetrieveAllAsync(query, ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0].Id;
    }

    private async Task<List<Entity>> LoadCodesAsync(Guid setId, CancellationToken ct)
    {
        var query = new QueryExpression(CodeEntity)
        {
            ColumnSet = new ColumnSet("sprk_code", "sprk_codetype", "sprk_description", "sprk_guidancetext"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_codeset", ConditionOperator.Equal, setId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        query.AddOrder("sprk_code", OrderType.Ascending);

        return await RetrieveAllAsync(query, ct).ConfigureAwait(false);
    }

    private static List<InvoiceCodeOption> ToOptions(IEnumerable<Entity> codes, BillingCodeKind kind)
    {
        var options = new List<InvoiceCodeOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in codes)
        {
            if (row.GetAttributeValue<OptionSetValue>("sprk_codetype")?.Value != (int)kind)
            {
                continue;
            }

            var code = row.GetAttributeValue<string>("sprk_code")?.Trim();
            if (string.IsNullOrEmpty(code) || !seen.Add(code))
            {
                continue;
            }

            var guidance = row.GetAttributeValue<string>("sprk_guidancetext");
            options.Add(new InvoiceCodeOption(
                code,
                row.GetAttributeValue<string>("sprk_description")?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(guidance) ? null : guidance.Trim()));
        }

        return options;
    }

    private async Task<List<Entity>> RetrieveAllAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize };
        var rows = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(page.Entities);

            if (!page.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }
}
