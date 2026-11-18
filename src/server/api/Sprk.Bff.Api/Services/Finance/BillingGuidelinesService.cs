// ebilling-r1 task 135 (FR-49, FR-28, D-66; notes/135-spa-billing-guidelines.md): the Billing Guidelines a firm sees in the external SPA.
//
// Component justification (CLAUDE.md section 11):
//   (1) Existing: ProcedureResolver (082) says which Published rules are in force for a firm (and, per record, a record); RuleBodyDescriber
//       says what a body means; RuleSetClauseVisibility (076) decides what clause text a firm may see; no route exposes policy content to a
//       CIAM caller (grep "BillingGuidelines" in src found none before this task).
//   (2) Extension: all four are reused unchanged in behaviour. This class only joins them: it adds the rule set's flag and the Procedure's
//       firm-facing label (two batched reads of ids the resolver already returned) and builds an explicit allow-list DTO.
//   (3) Cost of doing nothing: a firm cannot see the rules it is measured against (D-66); FR-49 fails.
//
// Placement (bff-extensions.md): BFF, Services/Finance, beside the other Finance read models. Concrete scoped class over the singleton
// app-only IGenericEntityService (B1 caller identity is decided by the route's filter, B4 live request); no package, no interface (ADR-010).
//
// What a firm is NEVER given (D-63, D-66, model 7.4): a document link, container id or drive item id (the DTO has no member for one); a
// Procedure id, a Procedure's internal name where a firm-facing name is set, a rule set's name, a policy code (a record rule's code carries
// its id), an override reason, a record id or name (a rule from a parent record is labelled "from the parent record" and never names it),
// or the refusal text of the describer (a refused rule is listed with its name and clause and a marker). Every DTO below is built field by field.

using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>One of the caller's own firms: its id and the name its sets are labelled with (a blank name reads as <see cref="BillingGuidelinesService.UnnamedFirm"/>).</summary>
public sealed record CallerFirm(Guid FirmId, string Name);

/// <summary>The Billing Guidelines view: the sets in force today, by firm.</summary>
public sealed record BillingGuidelinesResponse(IReadOnlyList<BillingGuidelineSet> Sets);

/// <summary>
/// One group of rules: a Procedure's published rules in force (<paramref name="Name"/> is its firm-facing name), or the rules set on the record
/// (<see cref="BillingGuidelinesService.RecordLabel"/>). <paramref name="Scope"/> is one of the closed labels of
/// <see cref="BillingGuidelinesService"/>. Dates are the Procedure version's (null for a record group).
/// </summary>
public sealed record BillingGuidelineSet(
    Guid FirmId, string FirmName, string Name, string Scope, int? Version, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<BillingGuidelineRule> Rules);

/// <summary>
/// One rule. A rule the describer refused has <paramref name="DescriptionAvailable"/> false, no description and no parameters: the name and the
/// clause only, never fallback text.
/// </summary>
public sealed record BillingGuidelineRule(
    string Name, bool DescriptionAvailable, string? Description, IReadOnlyList<BillingGuidelineParameter> Parameters,
    string? ClauseQuote, string? SectionReference, DateOnly? InForceFrom);

/// <summary>A parameter of the rule body: a label, the value as the body holds it, and its unit when the type has one.</summary>
public sealed record BillingGuidelineParameter(string Name, string Value, string? Unit);

/// <summary>See the file header.</summary>
public sealed class BillingGuidelinesService
{
    /// <summary>The label of the rules set on a record (the same neutral name 083 gives a record rule's clause).</summary>
    public const string RecordLabel = RecordRuleSet.Name;

    public const string ScopeAllRecords = "Applies to all records";
    public const string ScopeFirm = "Applies to your firm";
    public const string ScopeThisRecord = "Set for this record";
    public const string ScopeParentRecord = "From the parent record";

    /// <summary>The label of a firm that has no name. The firm-name read answers the firm's id when the name is blank; an id is never a label.</summary>
    public const string UnnamedFirm = "Your firm";

    /// <summary>The label of a Procedure rule whose policy has no name (a closed fallback; never a code).</summary>
    public const string UnnamedRule = "Billing rule";

    private const int MaxInValues = BillingFactLoader.MaxInValues;
    private const string VisibleColumn = "sprk_visibletofirms";

    private readonly IGenericEntityService _entities;
    private readonly RuleBodyDescriber _describer;
    private readonly ILogger<BillingGuidelinesService> _logger;

    public BillingGuidelinesService(IGenericEntityService entities, RuleBodyDescriber describer, ILogger<BillingGuidelinesService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The clock; a test sets it.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// The sets in force today for each of <paramref name="firms"/> (a caller of several firms sees each firm's, labelled with the firm), with
    /// no record (the general view) or for <paramref name="core"/> (the per-record view: the caller's Read on it is the route's decision, made
    /// before this is called).
    /// </summary>
    /// <exception cref="ProcedureResolutionFaultException">The Procedure rows could not be resolved. Operator text: never show its message.</exception>
    public async Task<BillingGuidelinesResponse> GetAsync(IReadOnlyList<CallerFirm> firms, CoreRecordReference? core, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(firms);
        var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var sets = new List<BillingGuidelineSet>();
        foreach (var firm in firms.Where(f => f.FirmId != Guid.Empty).DistinctBy(f => f.FirmId).OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.FirmId))
        {
            var rows = await ProcedureResolver.ReadRowsAsync(_entities, _logger, core, firm.FirmId, ct).ConfigureAwait(false);
            var onDate = new ProcedureResolution(rows).RulesOn(today);
            sets.AddRange(await ProjectAsync(firm, rows, onDate, ct).ConfigureAwait(false));
        }

        return new BillingGuidelinesResponse(sets);
    }

    /// <summary>The firm's name, or <see cref="UnnamedFirm"/> when it is blank or is the firm's own id (what the shared name read returns for a blank name).</summary>
    internal static string FirmLabel(CallerFirm firm) =>
        string.IsNullOrWhiteSpace(firm.Name) || (Guid.TryParse(firm.Name.Trim(), out var named) && named == firm.FirmId) ? UnnamedFirm : firm.Name;

    private async Task<IReadOnlyList<BillingGuidelineSet>> ProjectAsync(CallerFirm firm, ProcedureRows rows, ProcedureRulesOnDate onDate, CancellationToken ct)
    {
        // A regression guard BEHIND the resolver's own firm condition (O-20), which is what keeps another firm's rows out. A rule whose
        // Procedure or rule set the resolver's own rows do not list, or that belongs to another firm, is dropped (fail closed): whatever
        // the resolver returned, nothing of another firm is shown and nothing unexplained is shown.
        var procedureById = rows.Procedures.ToDictionary(p => p.Id);
        var setById = rows.Sets.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var rules = onDate.Rules.Where(r =>
                (r.RecordRule is not null
                    || (procedureById.TryGetValue(r.Procedure.ProcedureId, out var p) && (p.FirmId is null || p.FirmId == firm.FirmId)))
                && setById.TryGetValue(r.Version.RuleSetId, out var s) && (s.FirmId is null || s.FirmId == firm.FirmId))
            .ToList();
        if (rules.Count == 0)
        {
            return [];
        }

        var setRows = await ReadByIdsAsync("sprk_ruleset", "sprk_rulesetid", rules.Select(r => r.Version.RuleSetId),
            [VisibleColumn, "statuscode"], ct).ConfigureAwait(false);
        var procedureRows = await ReadByIdsAsync(ProcedureEntity.Entity, "sprk_procedureid",
            rules.Where(r => r.RecordRule is null).Select(r => r.Procedure.ProcedureId),
            [ProcedureEntity.FirmFacingNameColumn, ProcedureEntity.NameColumn], ct).ConfigureAwait(false);

        var groups = new Dictionary<string, (string Name, string Scope, int? Version, DateOnly? From, DateOnly? To, List<BillingGuidelineRule> Rules)>(StringComparer.Ordinal);
        foreach (var rule in rules.OrderBy(r => r.Version.PolicyName, StringComparer.Ordinal).ThenBy(r => r.Version.PolicyId))
        {
            // Only a Published set is shown (the view lists what is in force today: a Retired or Draft set is not).
            if (!setRows.TryGetValue(rule.Version.RuleSetId, out var set)
                || set.GetAttributeValue<OptionSetValue>("statuscode")?.Value != RuleSetService.RuleSetPublished
                || !RuleSetClauseVisibility.IsVisibleToFirms(set.GetAttributeValue<bool?>(VisibleColumn)))
            {
                continue;
            }

            string key, label, scope;
            int? version = null;
            DateOnly? from = null, to = null;
            if (rule.RecordRule is { } slot)
            {
                label = RecordLabel;
                scope = slot.ChainRecord.Distance == 0 ? ScopeThisRecord : ScopeParentRecord;
                key = "record:" + slot.ChainRecord.Distance.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var named = ProcedureEntity.FirmFacingLabelOf(procedureRows.GetValueOrDefault(rule.Procedure.ProcedureId));
                if (named is null)
                {
                    // 083: a Procedure that cannot be read or has no usable name shows no text. The id is logged; the label never is.
                    _logger.LogWarning("A Billing Guidelines Procedure has no readable name; its rules are not shown (procedure {ProcedureId})", rule.Procedure.ProcedureId);
                    continue;
                }

                label = named;
                scope = rule.Procedure.Rank.Level switch
                {
                    ProcedureLevel.ManualOwn => ScopeThisRecord,
                    ProcedureLevel.ManualParent => ScopeParentRecord,
                    ProcedureLevel.Firm => ScopeFirm,
                    _ => ScopeAllRecords,
                };
                key = "procedure:" + rule.Procedure.ProcedureId.ToString("N");
                version = rule.Procedure.ProcedureVersionNumber;
                var procedureVersion = rows.Versions.FirstOrDefault(v => v.Id == rule.Procedure.ProcedureVersionId);
                from = procedureVersion?.From;
                to = procedureVersion?.To;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = (label, scope, version, from, to, []);
                groups[key] = group;
            }

            group.Rules.Add(await ProjectRuleAsync(rule.Version, label, set, ct).ConfigureAwait(false));
        }

        return groups.Values
            .Select(g => new BillingGuidelineSet(firm.FirmId, FirmLabel(firm), g.Name, g.Scope, g.Version, g.From, g.To, g.Rules))
            .OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Scope, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<BillingGuidelineRule> ProjectRuleAsync(ResolvedPolicyVersion version, string label, Entity set, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(version.PolicyName) ? UnnamedRule : version.PolicyName.Trim();

        // The one clause decision (076): the flag, then blank text as null, then no text when there is neither a quote nor a section. The
        // flag was decided above for the whole set (the same helper), so it is passed as read.
        var clause = RuleSetClauseVisibility.ForFirm(set.GetAttributeValue<bool?>(VisibleColumn), version.ClauseReference, version.ClauseLocation, label);

        RuleDescriptionResult? described = null;
        try
        {
            described = await _describer.DescribeAsync(version.RuleBody, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("A Billing Guidelines rule could not be described ({ExceptionType}) (policy {PolicyId}, version {PolicyVersionId})",
                ex.GetType().Name, version.PolicyId, version.PolicyVersionId);
        }

        if (described?.Description is not { } description)
        {
            // FR-24: refused, never dropped and never described by fallback text. Ids are logged, the refusal text is not shown.
            if (described is not null)
            {
                _logger.LogWarning("The describer refused a Billing Guidelines rule (policy {PolicyId}, version {PolicyVersionId})", version.PolicyId, version.PolicyVersionId);
            }

            return new BillingGuidelineRule(name, false, null, [], clause.Quote, clause.SectionReference, version.InForceFrom);
        }

        return new BillingGuidelineRule(
            name, true, string.Join(" ", description.Clauses.Select(c => c.Text)), ParametersOf(version.RuleBody),
            clause.Quote, clause.SectionReference, version.InForceFrom);
    }

    // ── Parameters: names and values from the rule body, units from the closed table below ──

    /// <summary>
    /// Each parameter's label and unit, by the body's member name. A closed table: the schemas carry units only in prose, so
    /// <c>BillingGuidelinesParameterTableTests</c> reads every billing schema and fails when a member is neither here nor in
    /// <see cref="NotShown"/>. A member the table does not know is listed by its own name with no unit.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string Label, string? Unit)> ParameterTable = new Dictionary<string, (string, string?)>(StringComparer.Ordinal)
    {
        ["tolerancePercent"] = ("Tolerance", "percent"),
        ["incrementHours"] = ("Time increment", "hours"),
        ["maxHoursPerDay"] = ("Maximum hours per day", "hours"),
        ["maxAgeDays"] = ("Maximum age", "days"),
        ["graceDays"] = ("Grace period", "days"),
        ["discountPercent"] = ("Agreed discount", "percent"),
        ["capPercent"] = ("Cap", "percent of the invoice's other lines"),
        ["capAmount"] = ("Cap", "invoice currency"),
        ["maxAmountPerItem"] = ("Maximum per item", "invoice currency"),
        ["afterDays"] = ("Allowed after the due date by", "days"),
        ["minConfidence"] = ("Minimum confidence", null),
        ["observation"] = ("Observation", null),
        ["codes"] = ("Codes", null),
        ["nonReimbursableCodes"] = ("Non-reimbursable codes", null),
        ["feeCodes"] = ("Fee codes", null),
        ["matchFields"] = ("Match fields", null),
        ["fields"] = ("Required fields", null),
        ["feeKind"] = ("Fee kind", null),
        ["allowance"] = ("Allowance", null),
    };

    /// <summary>Body members that are never a parameter.</summary>
    internal static readonly IReadOnlySet<string> NotShown = new HashSet<string>(StringComparer.Ordinal) { "type" };

    private const int MaxListItems = 100;

    /// <summary>The scalar and list-of-scalar members of the body, in the body's order. Anything else (an object, null) is skipped.</summary>
    internal static IReadOnlyList<BillingGuidelineParameter> ParametersOf(string? ruleBody)
    {
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(ruleBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var parameters = new List<BillingGuidelineParameter>();
            foreach (var member in doc.RootElement.EnumerateObject().Where(m => !NotShown.Contains(m.Name)))
            {
                var value = ValueOf(member.Name, member.Value);
                if (value is null)
                {
                    continue;
                }

                var (label, unit) = ParameterTable.TryGetValue(member.Name, out var known) ? known : (Humanize(member.Name), null);
                parameters.Add(new BillingGuidelineParameter(label, value, unit));
            }

            return parameters;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ValueOf(string member, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => member == "feeKind" ? BillingRuleCatalogue.FeeKindLabel(element.GetString()) : element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Array => ListOf(element),
        _ => null,
    };

    private static string? ListOf(JsonElement array)
    {
        var items = array.EnumerateArray()
            .Where(i => i.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() : i.GetRawText())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToList();
        if (items.Count == 0)
        {
            return null;
        }

        return items.Count <= MaxListItems
            ? string.Join(", ", items)
            : string.Join(", ", items.Take(MaxListItems)) + $" (and {items.Count - MaxListItems} more)";
    }

    /// <summary>"someMemberName" as "Some member name".</summary>
    private static string Humanize(string member)
    {
        var words = new System.Text.StringBuilder();
        foreach (var c in member)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }

        return words.ToString();
    }

    // ── Batched reads of ids the resolver already returned ──

    private async Task<Dictionary<Guid, Entity>> ReadByIdsAsync(
        string table, string idColumn, IEnumerable<Guid> ids, string[] columns, CancellationToken ct)
    {
        var found = new Dictionary<Guid, Entity>();
        foreach (var chunk in ids.Where(i => i != Guid.Empty).Distinct().Order().Chunk(MaxInValues))
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), NoLock = true };
            query.Criteria.AddCondition(idColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
            {
                if (chunk.Contains(row.Id))
                {
                    found[row.Id] = row;
                }
            }
        }

        return found;
    }
}
