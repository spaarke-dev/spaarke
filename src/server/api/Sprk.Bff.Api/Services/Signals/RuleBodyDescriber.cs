using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Billing;
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
/// Turns an Existence, Threshold or billing (ebilling-r1 task 070) rule body into plain-language clauses, with lookup GUIDs shown as
/// names for Existence (task 026, spec FR-48/FR-57; v4-prototype-vs-solution #11). Read-side only: nothing is stored and no column
/// holds the result.
/// </summary>
/// <remarks>
/// <para><b>The words cannot drift from the filter.</b> The body is first run through
/// <see cref="PolicyVersionValidator.ValidateForSave"/>, so the describer refuses every body the validator refuses and
/// describes only shapes it accepts. For an Existence or Threshold body that is the schema and then the compiler. A billing body
/// (<see cref="RuleDomains.IsBilling"/>) is NEVER compiled (the compiler refuses every billing type): the validator runs the
/// billing checks instead (length cap, the type's schema, the template against the type's fact-field set) and the describer then
/// states it from <see cref="BillingRuleCatalogue.Describe"/>. The describer then re-reads the SAME text with the compiler's own
/// strict parser (<see cref="PredicateCompiler.ParseBounded"/>), so both see an identical document. An unknown shape is a refusal,
/// never a guess.</para>
/// <para><b>What the sentence claims is what the filter tests.</b> Every condition becomes one phrase; nothing is added.
/// A relative date is stated as the filter states it (<c>now-30d</c> is "30 days ago"), never softened into "recently".
/// A classifier-based clause (a <c>sprk_triagecategory</c> condition) is phrased as a classification, per v4 HANDOFF
/// section 1.4 (binding). A value with no known label is printed as the filter holds it (for example a choice value
/// 100000003), not invented. <c>statecode</c>'s two states are the same on every table and are said as "active" / "inactive"
/// (DI-159). Any other choice value tested for equality (<c>statuscode</c>, a choice column such as <c>sprk_reviewoutcome</c>) is said by
/// its option label (task 210, DI-165), read from the BFF's shared entity-metadata cache (<see cref="MetadataService"/>: one Dataverse
/// read per table org-wide per cache lifetime while the shared cache is reachable; if Redis is unavailable, MetadataService reads
/// Dataverse on each call, its existing behaviour for every caller). That read is made only when the body tests such a value, and a
/// failure shows the number instead: the description is never refused for it. The formal form always carries the exact value.</para>
/// <para><b>Column names.</b> A column is said by its Dataverse display name in lower case ("created on"), from the static
/// <see cref="ColumnDisplayNames"/> (the authorable columns are a closed, reviewed list, so the names need no metadata read).</para>
/// <para><b>Names.</b> Lookup GUIDs in the closed <see cref="LookupColumns"/> table are resolved by ONE batched read per
/// reference table, with the BFF's own <see cref="IGenericEntityService"/> (the evaluator's principal: reference rows
/// are configuration, not matter data). A GUID that does not resolve refuses the description rather than showing an
/// unlabelled id as if it were a name.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: none (grep "Describe" in Services/Signals
/// found no describer; <c>sprk_sentence</c> is the rule's literal template). Extension: no; the compiler's job is FetchXML,
/// and prose would give it a second reason to change. Cost of doing nothing: the wizard's "How this was determined" and
/// the admin rule page have no data, or hand-written text that drifts from the filter.</para>
/// <para><b>Label sources (task 210, CLAUDE.md section 11).</b> Existing: <see cref="MetadataService"/> already reads and caches every
/// table's option labels (state, status and picklist) org-wide in Redis for 6 hours; the describer reuses it and adds no cache, no
/// service and no Dataverse call of its own. Column display names extend the existing static label table (as <see cref="EntityLabels"/>
/// does for tables). Extension: <see cref="MetadataService"/> is used as it is. Cost of doing nothing: a decision says "status is
/// 659490001" and "where createdon is on or before 5 days ago" (#3020, #3040).</para>
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

    /// <summary>One label per table in <see cref="PredicateCompiler.EvaluatorGlobalReadableEntities"/> (pinned by a guard test): a table with none would be
    /// said by its logical name, "Each sprk_invoice where the sprk_invoice is active".</summary>
    internal static readonly IReadOnlyDictionary<string, string> EntityLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_matter"] = "matter",
        ["sprk_project"] = "project",
        ["sprk_event"] = "event",
        ["sprk_todo"] = "To Do",
        ["sprk_workassignment"] = "work assignment",
        ["sprk_communication"] = "communication",
        ["sprk_budgetrevision"] = "budget revision",
        ["sprk_budget"] = "budget",
        ["sprk_invoice"] = "invoice",
        ["sprk_memo"] = "memo",
        ["sprk_servicerequest"] = "service request",
    };

    /// <summary>
    /// Task 210 (DI-169): the Dataverse display name of every column in <see cref="PolicyVersionValidator.AuthorableColumns"/>, exactly as
    /// the solution source defines it (<c>SpaarkeMaster/Entities/*/Entity.xml</c>). A guard test pins both: every authorable column has one,
    /// and each equals the solution source. The rule wizard's column lists show these (the authoring catalog carries them); a sentence
    /// says them in lower case ("created on") unless <see cref="ColumnLabels"/> words the column differently. A column not listed is said
    /// by its logical name, never guessed.
    /// </summary>
    internal static readonly IReadOnlyDictionary<(string Entity, string Column), string> ColumnDisplayNames =
        new Dictionary<(string Entity, string Column), string>
        {
            [("sprk_communication", "sprk_triagecategory")] = "Triage Category",
            [("sprk_communication", "sprk_receiveddate")] = "Received Date",
            [("sprk_communication", "sprk_reviewoutcome")] = "Review Outcome",
            [("sprk_communication", "sprk_regardingmatter")] = "Regarding Matter",
            [("sprk_communication", "statecode")] = "Status",
            [("sprk_budgetrevision", "sprk_revisedon")] = "Revised On",
            [("sprk_budgetrevision", "sprk_matter")] = "Matter",
            [("sprk_budgetrevision", "statecode")] = "Status",
            [("sprk_budget", "sprk_matter")] = "Matter",
            [("sprk_budget", "sprk_budgetstartdate")] = "Budget Start Date",
            [("sprk_budget", "sprk_budgetenddate")] = "Budget End Date",
            [("sprk_budget", "statecode")] = "Status",
            [("sprk_event", "sprk_eventtype_ref")] = "Event Type",
            [("sprk_event", "statuscode")] = "Status Reason",
            [("sprk_event", "statecode")] = "Status",
            [("sprk_event", "sprk_approveddate")] = "Approved Date",
            [("sprk_event", "sprk_basedate")] = "Base Date",
            [("sprk_event", "sprk_completeddate")] = "Completed Date",
            [("sprk_event", "sprk_duedate")] = "Due Date",
            [("sprk_event", "sprk_finalduedate")] = "Final Due Date",
            [("sprk_event", "sprk_meetingdate")] = "Meeting Date",
            [("sprk_event", "sprk_tododuedate")] = "To Do Due Date",
            [("sprk_todo", "statuscode")] = "Status Reason",
            [("sprk_todo", "statecode")] = "Status",
            [("sprk_todo", "sprk_duedate")] = "Due Date",
            [("sprk_workassignment", "statecode")] = "Status",
            [("sprk_workassignment", "sprk_responseduedate")] = "Response Due Date",
            [("sprk_workassignment", "sprk_respondedon")] = "Responded On",
            // spaarke-intake-pipeline-r1 task 017: a service request's Date Only column, authorable once the table is a verified subject.
            [("sprk_servicerequest", "sprk_responseduedate")] = "Response Due Date",
            [("sprk_matter", "createdon")] = "Created On",
            [("sprk_matter", "statecode")] = "Status",
            [("sprk_matter", "sprk_matterid")] = "Matter",
            [("sprk_matter", "sprk_openeddate")] = "Opened Date",
            [("sprk_matter", "sprk_closeddate")] = "Closed Date",
            [("sprk_matter", "sprk_lastreviewdate")] = "Last Review Date",
            [("sprk_matter", "sprk_nextreviewdate")] = "Next Review Date",
            [("sprk_matter", "sprk_budgetutilizationpercent")] = "Budget Utilization Percent",
            [("sprk_matter", "sprk_totalbudget")] = "Total Budget",
            [("sprk_matter", "sprk_totalspendtodate")] = "Total Spend to Date",
            [("sprk_project", "statecode")] = "Status",
            [("sprk_project", "sprk_openeddate")] = "Opened Date",
            [("sprk_project", "sprk_closeddate")] = "Closed Date",
            [("sprk_project", "sprk_lastreviewdate")] = "Last Review Date",
            [("sprk_project", "sprk_nextreviewdate")] = "Next Review Date",
            [("sprk_project", "sprk_budgetutilizationpercent")] = "Budget Utilization Percent",
            [("sprk_project", "sprk_totalbudget")] = "Total Budget",
            [("sprk_project", "sprk_totalspendtodate")] = "Total Spend to Date",
            [("sprk_invoice", "statecode")] = "Status",
            [("sprk_invoice", "sprk_invoicedate")] = "Invoice Date",
            [("sprk_invoice", "sprk_invoiceduedate")] = "Invoice Due Date",
            [("sprk_memo", "statecode")] = "Status",
        };

    /// <summary>Sentence wording by column, ahead of the lower-cased display name (<see cref="ColumnDisplayNames"/>): a column worded
    /// differently from its display name ("status", "response recorded"), and the pre-task-210 table, kept so a column of a body written
    /// outside the BFF (not on the allow-list for its table) still reads as before.</summary>
    private static readonly IReadOnlyDictionary<string, string> ColumnLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_receiveddate"] = "received date",
        ["sprk_reviewoutcome"] = "review outcome",
        ["sprk_revisedon"] = "revised on",
        ["sprk_duedate"] = "due date",
        ["sprk_responseduedate"] = "response due date",
        // Read as "it has no response recorded" / "it has a response recorded" by a null test (task 061, D-54).
        ["sprk_respondedon"] = "response recorded",
        ["sprk_eventtype_ref"] = "event type",
        // "status is Open" (task 210), not "status reason is Open".
        ["statuscode"] = "status",
        ["statecode"] = "state",
        // A PERCENT (task 025b, D-122): 85 means 85 percent of the budget spent, 100 means fully spent. Said in the label so a
        // reader of "is at least 85" cannot take it for a fraction or an amount.
        ["sprk_budgetutilizationpercent"] = "budget utilization (percent of budget spent)",
        ["sprk_totalbudget"] = "total budget",
        ["sprk_totalspendtodate"] = "total spend to date",
    };

    /// <summary>(table, column) → (option value → label), for the choice values one body tests (task 210).</summary>
    private sealed class ChoiceLabels : Dictionary<(string Entity, string Column), IReadOnlyDictionary<int, string>>;

    private const string TriageCategoryColumn = "sprk_triagecategory";

    private const string StateCodeColumn = "statecode";

    private static readonly Regex RelativeDate = new(@"^now(?:(?<sign>[-+])(?<days>[0-9]{1,4})d)?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly PolicyVersionValidator _validator;
    private readonly IGenericEntityService _entities;
    private readonly ILogger<RuleBodyDescriber> _logger;
    private readonly IServiceScopeFactory? _scopes;

    /// <summary>A describer with no choice-label source: every choice value other than a <c>statecode</c> state is shown as its number.</summary>
    public RuleBodyDescriber(PolicyVersionValidator validator, IGenericEntityService entities, ILogger<RuleBodyDescriber> logger)
        : this(validator, entities, logger, scopes: null)
    {
    }

    /// <param name="scopes">Resolves the scoped <see cref="MetadataService"/> (the shared, cached entity metadata) for choice labels, the
    /// way <c>UpdateRecordActionCore</c> reaches it from a singleton. Null: no labels, numbers shown.</param>
    public RuleBodyDescriber(
        PolicyVersionValidator validator, IGenericEntityService entities, ILogger<RuleBodyDescriber> logger, IServiceScopeFactory? scopes)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _scopes = scopes;
    }

    public async Task<RuleDescriptionResult> DescribeAsync(string? ruleBodyJson, CancellationToken ct)
    {
        // The validator (ValidateForSave is its pure, side-effect-free entry) is the authority on what a body means; a body
        // it refuses is not described. For Existence / Threshold that is the schema, then the compiler (only ever reached
        // through the validator, PredicateCompilerCallerGuardTests); for a billing type it is the billing checks, with no compile.
        // The rule type is the body's own "type" member (the schema pins it to the type's name, so a stored version whose
        // column disagrees with its body is refused at evaluation and is not worth describing). Anything unreadable
        // falls back to Existence, whose validation then names the problem.
        // ebilling-r1 task 070 (FR-24 MUST): a "type" member that names no closed-set member is refused here, never described
        // as Existence. Only a body with no readable "type" at all still falls back to Existence (whose validation refuses it).
        if (BodyRuleType(ruleBodyJson) is not { } ruleType)
        {
            return new RuleDescriptionResult(null,
                $"The rule body's type is not a member of the closed rule-type set (reason: {OntologyWriterFailureReason.RuleTypeUnsupported}).");
        }

        var validation = _validator.ValidateForSave(ruleType.ToString(), ruleBodyJson, messageTemplate: null);
        if (!validation.IsValid)
        {
            return new RuleDescriptionResult(null,
                $"The rule body is not one the compiler accepts (reason: {validation.Reason}).");
        }

        try
        {
            using var doc = PredicateCompiler.ParseBounded(ruleBodyJson);
            if (RuleDomains.IsBilling(ruleType))
            {
                // Deterministic, one sentence per billing type, no lookup read (BillingRuleCatalogue.Describe).
                var (text, formal) = BillingRuleCatalogue.Describe(ruleType, doc.RootElement);
                return new RuleDescriptionResult(
                    new RuleDescription(new[] { new RuleClauseDescription("rule", ruleType.ToString(), text, formal) }), null);
            }

            // Task 210 (DI-165): option labels only for the choice values this body tests; none read when it tests none.
            var choices = await ReadChoiceLabelsAsync(ChoiceCandidates(doc.RootElement), ct).ConfigureAwait(false);
            return ruleType == RuleType.Threshold
                ? DescribeThreshold(doc.RootElement, choices)
                : await DescribeParsedAsync(doc.RootElement, choices, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested)
                                   && ex is not PredicateCompilationException)
        {
            // A reference-table read fault must not turn a plan read into a 500: the description is an extra, so it is
            // refused (logged with the exception type, metered by reason) and the caller serves the plan without it. Only the
            // CALLER's cancellation propagates; a timeout-style TaskCanceledException is a fault like any other.
            return Refused(RuleDescriptionRefusalReason.LookupReadFailed,
                "A lookup name could not be read, so the rule cannot be described right now.", ex);
        }
        catch (PredicateCompilationException ex)
        {
            // Unreachable for a body the validator accepted; kept so a future drift between the two fails closed.
            return new RuleDescriptionResult(null, "The rule body could not be read: " + ex.Message);
        }
    }

    /// <summary>The closed-set member the body's <c>"type"</c> names, by exact (ordinal) name; Existence when the body has no
    /// <c>"type"</c> member or does not parse (its validation then names the problem); <c>null</c> when a <c>"type"</c> member
    /// is present but names no member (a refusal, ebilling-r1 task 070).</summary>
    private static RuleType? BodyRuleType(string? ruleBodyJson)
    {
        try
        {
            using var doc = PredicateCompiler.ParseBounded(ruleBodyJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("type", out var type))
            {
                return RuleType.Existence;
            }

            if (type.ValueKind == JsonValueKind.String && type.GetString() is { } name)
            {
                foreach (var member in Enum.GetValues<RuleType>())
                {
                    if (string.Equals(name, member.ToString(), StringComparison.Ordinal)) return member;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is PredicateCompilationException or JsonException or InvalidOperationException)
        {
            return RuleType.Existence;
        }
    }

    /// <summary>
    /// A Threshold body (task 025): the optional <c>when</c> scope line, then one <c>threshold</c> line stating the single
    /// comparison. No lookup names are read (the compared column is numeric and the shared <c>when</c> grammar is described by
    /// the same phrases as Existence), so nothing here can fail on a reference read.
    /// </summary>
    private static RuleDescriptionResult DescribeThreshold(JsonElement root, ChoiceLabels choices)
    {
        var subject = root.GetProperty("subject").GetString()!;
        var subjectLabel = Label(EntityLabels, subject);
        var lines = new List<RuleClauseDescription>();

        if (root.TryGetProperty("when", out var when) && when.ValueKind == JsonValueKind.Object && when.EnumerateObject().Any())
        {
            var phrases = Phrases(subject, when, new Dictionary<Guid, string>(), choices);
            lines.Add(new RuleClauseDescription("when", "Subject",
                $"Each {subjectLabel} where {Join(phrases.Words)}.",
                $"{subject} where {string.Join(" and ", phrases.Formal)}"));
        }

        var threshold = root.GetProperty("threshold");
        var field = threshold.GetProperty("field").GetString()!;
        var op = threshold.GetProperty("operator").GetString()!;
        var value = threshold.GetProperty("value");
        var comparison = op switch
        {
            ">=" => "is at least",
            ">" => "is more than",
            "<=" => "is at most",
            "<" => "is less than",
            _ => throw new PredicateCompilationException($"threshold.operator: '{op}' has no description."),
        };
        lines.Add(new RuleClauseDescription("threshold", "Threshold",
            $"The {subjectLabel}'s {ColumnLabel(subject, field)} {comparison} {Raw(value)}.",
            $"{subject}.{field} {op} {Raw(value)}"));

        return new RuleDescriptionResult(new RuleDescription(lines), null);
    }

    private RuleDescriptionResult Refused(string reason, string message, Exception? fault = null)
    {
        // The exception TYPE only, never its message (it can echo row content). Not a metric dimension: the metric stays
        // bounded by reason.
        _logger.LogWarning(OntologyWriterEvents.RuleDescriptionRefused,
            "Rule description refused (reason={Reason}, exceptionType={ExceptionType}).", reason, fault?.GetType().Name ?? "none");
        OntologyWriterTelemetry.RecordRuleDescriptionRefused(reason);
        return new RuleDescriptionResult(null, message);
    }

    private async Task<RuleDescriptionResult> DescribeParsedAsync(JsonElement root, ChoiceLabels choices, CancellationToken ct)
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
            var phrases = Phrases(subject, when, names, choices);
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
            var phrases = Phrases(related, clause.GetProperty("filter"), names, choices);
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

    private static (List<string> Words, List<string> Formal) Phrases(
        string entity, JsonElement filter, Dictionary<Guid, string> names, ChoiceLabels choices)
    {
        var words = new List<string>();
        var formal = new List<string>();
        foreach (var field in filter.EnumerateObject())
        {
            var label = ColumnLabel(entity, field.Name);
            var isLookup = LookupColumns.ContainsKey((entity, field.Name));
            var options = choices.TryGetValue((entity, field.Name), out var known) ? known : null;
            var value = field.Value;

            if (value.ValueKind == JsonValueKind.Object)
            {
                var ops = value.EnumerateObject().ToList();
                if (ops.Count == 1 && ops[0].Name is PredicateCompiler.NullTestMember or PredicateCompiler.NotNullTestMember)
                {
                    // Task 061 (D-54): a null test names no value. "it has no response recorded" / "it has a response recorded";
                    // the formal form is the test itself.
                    var isNull = ops[0].Name == PredicateCompiler.NullTestMember;
                    words.Add(isNull ? $"it has no {label}" : $"it has {Article(label)} {label}");
                    formal.Add($"{field.Name} is {(isNull ? "null" : "not null")}");
                    continue;
                }

                // DI-159: the object form of a state test ({"=": 0}, {"<>": 1}, which the rule wizard writes) is said by the state's label too.
                if (field.Name == StateCodeColumn && ops.Count == 1 && ops[0].Name is ("=" or "<>") && StateWord(ops[0].Value) is { } opState)
                {
                    words.Add($"the {Label(EntityLabels, entity)} is {(ops[0].Name == "<>" ? "not " : string.Empty)}{opState}");
                    formal.Add($"{field.Name} {ops[0].Name} {Raw(ops[0].Value)}");
                    continue;
                }

                // A choice label only for an equality test: "is at least Open" would compare words, not the option values.
                words.Add(ops.Count == 2
                    ? RangeWords(label, ops)
                    : $"{label} {OperatorWords(ops[0].Name, ops[0].Value, field.Name, isLookup, names, ops[0].Name is "=" or "<>" ? options : null)}");
                formal.AddRange(ops.Select(o => $"{field.Name} {o.Name} {Raw(o.Value)}"));
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                var shown = value.EnumerateArray().Select(v => Display(v, isLookup, names, options)).ToList();
                if (field.Name == StateCodeColumn && value.EnumerateArray().All(v => StateWord(v) is not null))
                {
                    words.Add($"the {Label(EntityLabels, entity)} is {OrList(value.EnumerateArray().Select(v => StateWord(v)!).ToList())}");
                    formal.Add($"{field.Name} in ({string.Join(", ", value.EnumerateArray().Select(Raw))})");
                    continue;
                }

                words.Add(field.Name == TriageCategoryColumn
                    ? $"it was classified as {OrList(shown)}"
                    : $"{label} is {OrList(shown)}");
                formal.Add($"{field.Name} in ({string.Join(", ", value.EnumerateArray().Select(Raw))})");
                continue;
            }

            if (field.Name == StateCodeColumn && StateWord(value) is { } stateWord)
            {
                // DI-159: statecode is the same two-state choice on every table the rule grammar reads (0 Active, 1 Inactive), so it is
                // said by its label, never as "state is 0". The formal line below keeps the exact test.
                words.Add($"the {Label(EntityLabels, entity)} is {stateWord}");
                formal.Add($"{field.Name} = {Raw(value)}");
                continue;
            }

            words.Add(field.Name == TriageCategoryColumn
                ? $"it was classified as {Display(value, isLookup, names, options)}"
                : $"{label} is {Display(value, isLookup, names, options)}");
            formal.Add($"{field.Name} = {Raw(value)}");
        }

        return (words, formal);
    }

    /// <summary>The label of a <c>statecode</c> value: 0 is "active", 1 is "inactive"; null for any other value (shown as the filter holds it).</summary>
    private static string? StateWord(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var state)
            ? state switch { 0 => "active", 1 => "inactive", _ => null }
            : null;

    private static string OperatorWords(
        string op, JsonElement operand, string column, bool isLookup, Dictionary<Guid, string> names, IReadOnlyDictionary<int, string>? options = null)
    {
        var shown = Display(operand, isLookup, names, options);
        if (operand.ValueKind == JsonValueKind.Number)
        {
            // A numeric operand is a quantity, never a date: "is at least 5", not "is on or after 5".
            return op switch
            {
                ">=" => $"is at least {shown}",
                ">" => $"is more than {shown}",
                "<=" => $"is at most {shown}",
                "<" => $"is less than {shown}",
                "<>" => $"is not {shown}",
                "=" => $"is {shown}",
                _ => throw new PredicateCompilationException($"{column}: operator '{op}' has no description."),
            };
        }

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

    /// <param name="options">The column's option labels (task 210), or null: a whole-number value with a label is shown by the label.</param>
    private static string Display(JsonElement value, bool isLookup, Dictionary<Guid, string> names, IReadOnlyDictionary<int, string>? options = null)
    {
        if (options is not null && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var option)
            && options.TryGetValue(option, out var optionLabel))
        {
            return optionLabel;
        }

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

    /// <summary>A string as its text; a number in the compiler's canonical form (<c>PredicateCompiler.FormatScalar</c>:
    /// plain invariant decimal), so "1e2" reads 100 and a value beyond 28 digits reads the rounded value the query
    /// compares; anything else as raw JSON.</summary>
    private static string Raw(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number.ToString(CultureInfo.InvariantCulture),
        _ => value.GetRawText(),
    };

    private static string Label(IReadOnlyDictionary<string, string> labels, string logicalName) =>
        labels.TryGetValue(logicalName, out var label) ? label : logicalName;

    /// <summary>The indefinite article for a lower-case column label: "an opened date", "a due date" (task 210 fix round 1). No
    /// authorable label starts with a silent letter or a "you" sound, so the first letter decides.</summary>
    internal static string Article(string label) =>
        label.Length > 0 && "aeiouAEIOU".Contains(label[0]) ? "an" : "a";

    /// <summary>How a sentence names a column (task 210, DI-169): its <see cref="ColumnLabels"/> wording, else its display name in lower
    /// case ("created on"), else its logical name (a column outside <see cref="ColumnDisplayNames"/>; never guessed).</summary>
    internal static string ColumnLabel(string entity, string column) =>
        ColumnLabels.TryGetValue(column, out var phrase) ? phrase
        : ColumnDisplayNames.TryGetValue((entity, column), out var display) ? display.ToLowerInvariant()
        : column;

    /// <summary>
    /// The (table, column) pairs whose option labels the body's words need (task 210, DI-165): a column tested for equality with a whole
    /// number (a bare value, <c>{"=": n}</c>, <c>{"&lt;&gt;": n}</c>, or a list) that is not <c>statecode</c> (said as "active" /
    /// "inactive" without a read) and not a lookup. A Threshold's compared column is a quantity and is never one. Empty for most bodies.
    /// </summary>
    private static HashSet<(string Entity, string Column)> ChoiceCandidates(JsonElement root)
    {
        var wanted = new HashSet<(string Entity, string Column)>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("subject", out var subjectEl)
            || subjectEl.ValueKind != JsonValueKind.String || subjectEl.GetString() is not { } subject)
        {
            return wanted;
        }

        void Scan(string entity, JsonElement filter)
        {
            if (filter.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var field in filter.EnumerateObject())
            {
                if (field.Name == StateCodeColumn || LookupColumns.ContainsKey((entity, field.Name)))
                {
                    continue;
                }

                var value = field.Value;
                IEnumerable<JsonElement> tested = value.ValueKind switch
                {
                    JsonValueKind.Number => [value],
                    JsonValueKind.Array => value.EnumerateArray(),
                    JsonValueKind.Object => value.EnumerateObject().Where(o => o.Name is "=" or "<>").Select(o => o.Value),
                    _ => [],
                };
                if (tested.Any(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out _)))
                {
                    wanted.Add((entity, field.Name));
                }
            }
        }

        if (root.TryGetProperty("when", out var when))
        {
            Scan(subject, when);
        }

        if (root.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
        {
            foreach (var clause in all.EnumerateArray())
            {
                if (clause.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var hasRelated = clause.TryGetProperty("exists", out var related) || clause.TryGetProperty("notExists", out related);
                if (hasRelated && related.ValueKind == JsonValueKind.String && clause.TryGetProperty("filter", out var filter))
                {
                    Scan(related.GetString()!, filter);
                }
            }
        }

        return wanted;
    }

    /// <summary>
    /// The option labels of the wanted columns, from <see cref="MetadataService"/> (the shared Redis copy of each table's metadata, 6-hour
    /// lifetime; Dataverse is read only on a miss, once per table org-wide). One call per table, none when nothing is wanted. Fails soft: a
    /// table whose metadata cannot be read (or a describer with no <see cref="IServiceScopeFactory"/>) gives no labels, so its values are
    /// shown as numbers; only the CALLER's cancellation propagates.
    /// </summary>
    private async Task<ChoiceLabels> ReadChoiceLabelsAsync(HashSet<(string Entity, string Column)> wanted, CancellationToken ct)
    {
        var labels = new ChoiceLabels();
        if (wanted.Count == 0 || _scopes is null)
        {
            return labels;
        }

        foreach (var table in wanted.GroupBy(w => w.Entity, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); // no read for a caller that has gone
            try
            {
                using var scope = _scopes.CreateScope();
                var metadata = await scope.ServiceProvider.GetRequiredService<MetadataService>()
                    .GetMetadataAsync(table.Key, ct).ConfigureAwait(false);
                foreach (var (entity, column) in table)
                {
                    var optionSet = metadata.Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, column, StringComparison.Ordinal))?.OptionSet;
                    if (optionSet is null)
                    {
                        continue;
                    }

                    var byValue = new Dictionary<int, string>();
                    foreach (var option in optionSet.Options.Where(o => !string.IsNullOrWhiteSpace(o.Label)))
                    {
                        byValue.TryAdd(option.Value, option.Label);
                    }

                    labels[(entity, column)] = byValue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The exception TYPE only (its message can echo request detail); the values read as numbers.
                _logger.LogWarning(OntologyWriterEvents.RuleDescriptionLabelsUnavailable,
                    "Rule description shows choice values as numbers: the option labels could not be read (entity={Entity}, exceptionType={ExceptionType}).",
                    table.Key, ex.GetType().Name);
            }
        }

        return labels;
    }

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
