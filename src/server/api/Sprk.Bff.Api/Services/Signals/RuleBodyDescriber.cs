using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>One clause of a rule body in words and in formal form. <see cref="ClauseId"/> is the id evidence lines cite
/// (FR-48): <c>when</c> for the subject's own filter, <c>all[N]</c> for the N-th exists/notExists clause.</summary>
public sealed record RuleClauseDescription(string ClauseId, string Kind, string Text, string Formal);

/// <summary>A rule body described, one line per clause. Generated on read, never stored.</summary>
public sealed record RuleDescription(IReadOnlyList<RuleClauseDescription> Clauses);

/// <summary>Exactly one of <see cref="Description"/> / <see cref="Refusal"/> is set.</summary>
public sealed record RuleDescriptionResult(RuleDescription? Description, string? Refusal)
{
    public bool IsRefused => Description is null;
}

/// <summary>
/// Turns an Existence rule body into plain-language clauses, with lookup GUIDs shown as names (task 026, spec FR-48/FR-57;
/// v4-prototype-vs-solution #11). Read-side only: nothing is stored and no column holds the result.
/// </summary>
/// <remarks>
/// <para><b>The words cannot drift from the filter.</b> The body is first run through
/// <see cref="PolicyVersionValidator.ValidateForSave"/> (schema, then the compiler), so the describer refuses every body
/// the compiler refuses and describes only shapes the compiler accepts; it then re-reads the SAME text with the compiler's own strict parser
/// (<see cref="PredicateCompiler.ParseBounded"/>), so both see an identical document. An unknown shape is a refusal,
/// never a guess.</para>
/// <para><b>What the sentence claims is what the filter tests.</b> Every condition becomes one phrase; nothing is added.
/// A relative date is stated as the filter states it (<c>now-30d</c> is "30 days ago"), never softened into "recently".
/// A classifier-based clause (a <c>sprk_triagecategory</c> condition) is phrased as a classification, per v4 HANDOFF
/// section 1.4 (binding). A value with no known label is printed as the filter holds it (for example a choice value
/// 100000003), not invented. Known gap: choice option labels need a metadata read this task does not make; the formal
/// form always carries the exact value.</para>
/// <para><b>Names.</b> Lookup GUIDs in the closed <see cref="LookupColumns"/> table are resolved by ONE batched read per
/// reference table, with the BFF's own <see cref="IGenericEntityService"/> (the evaluator's principal: reference rows
/// are configuration, not matter data). A GUID that does not resolve refuses the description rather than showing an
/// unlabelled id as if it were a name.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: none (grep "Describe" in Services/Signals
/// found no describer; <c>sprk_sentence</c> is the rule's literal template). Extension: no; the compiler's job is FetchXML,
/// and prose would give it a second reason to change. Cost of doing nothing: the wizard's "How this was determined" and
/// the admin rule page have no data, or hand-written text that drifts from the filter.</para>
/// </remarks>
public sealed class RuleBodyDescriber
{
    /// <summary>(entity, column) → (reference table, name column) for lookup columns whose GUIDs are shown as names.</summary>
    internal static readonly IReadOnlyDictionary<(string Entity, string Column), (string RefTable, string NameColumn)> LookupColumns =
        new Dictionary<(string, string), (string, string)>
        {
            [("sprk_communication", "sprk_triagecategory")] = ("sprk_triagecategory", "sprk_name"),
            [("sprk_event", "sprk_eventtype_ref")] = ("sprk_eventtype_ref", "sprk_name"),
        };

    private static readonly IReadOnlyDictionary<string, string> EntityLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_matter"] = "matter",
        ["sprk_event"] = "event",
        ["sprk_todo"] = "To Do",
        ["sprk_workassignment"] = "work assignment",
        ["sprk_communication"] = "communication",
        ["sprk_budgetrevision"] = "budget revision",
        ["sprk_budget"] = "budget",
    };

    private static readonly IReadOnlyDictionary<string, string> ColumnLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_receiveddate"] = "received date",
        ["sprk_reviewoutcome"] = "review outcome",
        ["sprk_revisedon"] = "revised on",
        ["sprk_duedate"] = "due date",
        ["sprk_responseduedate"] = "response due date",
        ["sprk_eventtype_ref"] = "event type",
        ["statuscode"] = "status",
        ["statecode"] = "state",
    };

    private const string TriageCategoryColumn = "sprk_triagecategory";

    private static readonly Regex RelativeDate = new(@"^now(?:(?<sign>[-+])(?<days>[0-9]{1,4})d)?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly PolicyVersionValidator _validator;
    private readonly IGenericEntityService _entities;
    private readonly ILogger<RuleBodyDescriber> _logger;

    public RuleBodyDescriber(PolicyVersionValidator validator, IGenericEntityService entities, ILogger<RuleBodyDescriber> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    public async Task<RuleDescriptionResult> DescribeAsync(string? ruleBodyJson, CancellationToken ct)
    {
        // The validator (schema, then the compiler: ValidateForSave is its pure, side-effect-free entry) is the authority
        // on what a body means; a body it refuses is not described. The compiler is only ever reached through the
        // validator (PredicateCompilerCallerGuardTests).
        var validation = _validator.ValidateForSave(nameof(RuleType.Existence), ruleBodyJson, messageTemplate: null);
        if (!validation.IsValid)
        {
            return new RuleDescriptionResult(null,
                $"The rule body is not one the compiler accepts (reason: {validation.Reason}).");
        }

        try
        {
            using var doc = PredicateCompiler.ParseBounded(ruleBodyJson);
            return await DescribeParsedAsync(doc.RootElement, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not PredicateCompilationException)
        {
            // A reference-table read fault must not turn a plan read into a 500: the description is an extra, so it is
            // refused (logged, metered, type only) and the caller serves the plan without it.
            return Refused(RuleDescriptionRefusalReason.LookupReadFailed,
                "A lookup name could not be read, so the rule cannot be described right now.");
        }
        catch (PredicateCompilationException ex)
        {
            // Unreachable for a body the validator accepted; kept so a future drift between the two fails closed.
            return new RuleDescriptionResult(null, "The rule body could not be read: " + ex.Message);
        }
    }

    private RuleDescriptionResult Refused(string reason, string message)
    {
        _logger.LogWarning(OntologyWriterEvents.RuleDescriptionRefused, "Rule description refused (reason={Reason}).", reason);
        OntologyWriterTelemetry.RecordRuleDescriptionRefused(reason);
        return new RuleDescriptionResult(null, message);
    }

    private async Task<RuleDescriptionResult> DescribeParsedAsync(JsonElement root, CancellationToken ct)
    {
        var subject = root.GetProperty("subject").GetString()!;
        var ids = new Dictionary<(string Entity, string Column), HashSet<Guid>>();

        var whenFilter = root.TryGetProperty("when", out var w) && w.ValueKind == JsonValueKind.Object && w.EnumerateObject().Any()
            ? w : (JsonElement?)null;
        var clauses = root.GetProperty("all").EnumerateArray().ToList();

        if (whenFilter is { } wf)
        {
            Collect(subject, wf, ids);
        }

        foreach (var clause in clauses)
        {
            var related = (clause.TryGetProperty("exists", out var e) ? e : clause.GetProperty("notExists")).GetString()!;
            Collect(related, clause.GetProperty("filter"), ids);
        }

        var names = await ResolveNamesAsync(ids, ct).ConfigureAwait(false);
        if (names is null)
        {
            return Refused(RuleDescriptionRefusalReason.LookupUnresolved,
                "A lookup value in the rule body does not resolve to a name, so it cannot be described without guessing.");
        }

        var lines = new List<RuleClauseDescription>();
        if (whenFilter is { } when)
        {
            var phrases = Phrases(subject, when, names);
            lines.Add(new RuleClauseDescription("when", "Subject",
                $"Each {Label(EntityLabels, subject)} where {Join(phrases.Words)}.",
                $"{subject} where {string.Join(" and ", phrases.Formal)}"));
        }

        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var isExists = clause.TryGetProperty("exists", out var existsEl);
            var related = (isExists ? existsEl : clause.GetProperty("notExists")).GetString()!;
            var path = clause.GetProperty("path").GetString()!;
            var phrases = Phrases(related, clause.GetProperty("filter"), names);
            var subjectLabel = Label(EntityLabels, subject);
            var relatedLabel = Label(EntityLabels, related);

            var text = isExists
                ? $"A {relatedLabel} exists for the {subjectLabel} where {Join(phrases.Words)}."
                : $"No {relatedLabel} exists for the {subjectLabel} where {Join(phrases.Words)}.";
            var formal = $"{(isExists ? "exists" : "notExists")} {related} via {path} where {string.Join(" and ", phrases.Formal)}";
            lines.Add(new RuleClauseDescription($"all[{i}]", isExists ? "Exists" : "NotExists", text, formal));
        }

        return new RuleDescriptionResult(new RuleDescription(lines), null);
    }

    private static void Collect(string entity, JsonElement filter, Dictionary<(string, string), HashSet<Guid>> ids)
    {
        foreach (var field in filter.EnumerateObject())
        {
            if (!LookupColumns.ContainsKey((entity, field.Name)))
            {
                continue;
            }

            var set = ids.TryGetValue((entity, field.Name), out var existing) ? existing : ids[(entity, field.Name)] = [];
            foreach (var value in Values(field.Value))
            {
                if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id))
                {
                    set.Add(id);
                }
            }
        }
    }

    private static IEnumerable<JsonElement> Values(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray(),
        JsonValueKind.Object => value.EnumerateObject().Select(p => p.Value),
        _ => [value],
    };

    /// <summary>One batched read per reference table. Returns null when any collected id has no name row.</summary>
    private async Task<Dictionary<Guid, string>?> ResolveNamesAsync(
        Dictionary<(string Entity, string Column), HashSet<Guid>> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var byTable in ids.Where(kv => kv.Value.Count > 0).GroupBy(kv => LookupColumns[kv.Key]))
        {
            var wanted = byTable.SelectMany(kv => kv.Value).Distinct().ToList();
            var query = new QueryExpression(byTable.Key.RefTable) { ColumnSet = new ColumnSet(byTable.Key.NameColumn) };
            query.Criteria.AddCondition(byTable.Key.RefTable + "id", ConditionOperator.In, wanted.Cast<object>().ToArray());

            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in rows.Entities)
            {
                var name = row.GetAttributeValue<string>(byTable.Key.NameColumn);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names[row.Id] = name;
                }
            }

            if (wanted.Any(id => !names.ContainsKey(id)))
            {
                return null;
            }
        }

        return names;
    }

    private static (List<string> Words, List<string> Formal) Phrases(string entity, JsonElement filter, Dictionary<Guid, string> names)
    {
        var words = new List<string>();
        var formal = new List<string>();
        foreach (var field in filter.EnumerateObject())
        {
            var label = Label(ColumnLabels, field.Name);
            var isLookup = LookupColumns.ContainsKey((entity, field.Name));
            var value = field.Value;

            if (value.ValueKind == JsonValueKind.Object)
            {
                var ops = value.EnumerateObject().ToList();
                words.Add(ops.Count == 2 ? RangeWords(label, ops) : $"{label} {OperatorWords(ops[0].Name, ops[0].Value, field.Name, isLookup, names)}");
                formal.AddRange(ops.Select(o => $"{field.Name} {o.Name} {Raw(o.Value)}"));
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                var shown = value.EnumerateArray().Select(v => Display(v, isLookup, names)).ToList();
                words.Add(field.Name == TriageCategoryColumn
                    ? $"it was classified as {OrList(shown)}"
                    : $"{label} is {OrList(shown)}");
                formal.Add($"{field.Name} in ({string.Join(", ", value.EnumerateArray().Select(Raw))})");
                continue;
            }

            words.Add(field.Name == TriageCategoryColumn
                ? $"it was classified as {Display(value, isLookup, names)}"
                : $"{label} is {Display(value, isLookup, names)}");
            formal.Add($"{field.Name} = {Raw(value)}");
        }

        return (words, formal);
    }

    private static string OperatorWords(string op, JsonElement operand, string column, bool isLookup, Dictionary<Guid, string> names)
    {
        var shown = Display(operand, isLookup, names);
        return op switch
        {
            ">=" => $"is on or after {shown}",
            ">" => $"is after {shown}",
            "<=" => $"is on or before {shown}",
            "<" => $"is before {shown}",
            "<>" => $"is not {shown}",
            "=" => $"is {shown}",
            _ => throw new PredicateCompilationException($"{column}: operator '{op}' has no description."),
        };
    }

    /// <summary>The D-40 two-bound range: one lower, one upper, each inclusive or not as the operators say.</summary>
    private static string RangeWords(string label, List<JsonProperty> ops)
    {
        var lower = ops.First(o => o.Name is ">=" or ">");
        var upper = ops.First(o => o.Name is "<=" or "<");
        return $"{label} {OperatorWords(lower.Name, lower.Value, label, false, [])} and {OperatorWords(upper.Name, upper.Value, label, false, [])}";
    }

    private static string Display(JsonElement value, bool isLookup, Dictionary<Guid, string> names)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (isLookup && Guid.TryParse(text, out var id) && names.TryGetValue(id, out var name))
            {
                return name;
            }

            var match = RelativeDate.Match(text);
            if (match.Success)
            {
                if (!match.Groups["days"].Success)
                {
                    return "now";
                }

                var days = int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture);
                var unit = days == 1 ? "day" : "days";
                return match.Groups["sign"].Value == "-" ? $"{days} {unit} ago" : $"{days} {unit} from now";
            }

            return text;
        }

        return Raw(value);
    }

    private static string Raw(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static string Label(IReadOnlyDictionary<string, string> labels, string logicalName) =>
        labels.TryGetValue(logicalName, out var label) ? label : logicalName;

    private static string OrList(List<string> items) => string.Join(" or ", items);

    private static string Join(List<string> words)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(i == words.Count - 1 ? (words.Count == 2 ? " and " : ", and ") : ", ");
            }

            sb.Append(words[i]);
        }

        return sb.ToString();
    }
}
