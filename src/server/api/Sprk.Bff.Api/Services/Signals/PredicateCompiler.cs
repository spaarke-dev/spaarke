using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The output of <see cref="PredicateCompiler.Compile"/>: ONE FetchXML query whose result set is exactly the
/// subjects for which every clause of an <c>Existence</c> rule body holds (spec FR-06; design.md CM-3).
/// </summary>
/// <param name="SubjectEntity">The subject entity logical name (e.g. <c>sprk_matter</c>).</param>
/// <param name="SubjectIdAttribute">The subject primary-key column the query projects (e.g. <c>sprk_matterid</c>).
/// Each returned row carries exactly this one column.</param>
/// <param name="FetchXml">The single compiled query. Execute it as-is via
/// <c>IGenericEntityService.RetrieveMultipleAsync(new FetchExpression(FetchXml))</c>.</param>
/// <param name="WindowAnchorUtc">The instant every relative-date token (<c>now</c>, <c>now-30d</c>) was resolved
/// against. Recorded so a caller can state, truthfully, which window a Signal was evaluated over (§0.3).</param>
/// <param name="TemplateEligibleFields">The field names a <c>sprk_messagetemplate</c> <c>{{token}}</c> may
/// reference — the ONE definition of the §0.3 template vocabulary (task 022 rework round 2, finding F3,
/// coordinator decision). A token's value must be exactly ONE well-defined value for the firing subject, so a
/// field is eligible only when:
/// <list type="bullet">
/// <item>it is read by the subject's own <c>when</c> filter — with ANY operator, because the subject is one row
/// whose own value the evaluator reads; or</item>
/// <item>it is read by an <c>exists</c> clause that PINS it to exactly one value — a bare scalar, <c>{"=": v}</c>,
/// or an <c>in</c> list of exactly one element. A range/<c>&lt;&gt;</c>/multi-value <c>in</c> filter can match N
/// related rows carrying N different values, so "the" value would be undefined. For such a token the value IS
/// the clause's pinned literal. If the field is read by more than one <c>exists</c> clause, EVERY occurrence
/// must pin it and all pins must agree.</item>
/// </list>
/// Never a <c>notExists</c> clause's field: a firing subject has, by definition, no matching row there, so
/// only the fact of absence is known (literal template text can state that).</param>
/// <param name="AmbiguousTemplateFields">Field names refused as ambiguous: read in a positive position (<c>when</c>
/// or an <c>exists</c> clause) on MORE than one distinct entity, read by BOTH the subject's <c>when</c> and an
/// <c>exists</c> clause (two different rows even if the same table), or pinned by two <c>exists</c> clauses to
/// DIFFERENT values. Disjoint from the other two sets; consumed by <see cref="PolicyVersionValidator"/> to give
/// the author a specific "ambiguous" refusal.</param>
/// <param name="UnpinnedTemplateFields">Field names read only by <c>exists</c> clause(s), on one entity, where at
/// least one occurrence does NOT pin the field to exactly one value (finding F3). Disjoint from the other two
/// sets; consumed by <see cref="PolicyVersionValidator"/> to give the author a specific "not pinned" refusal.</param>
public sealed record CompiledPredicate(
    string SubjectEntity,
    string SubjectIdAttribute,
    string FetchXml,
    DateTimeOffset WindowAnchorUtc,
    IReadOnlySet<string> TemplateEligibleFields,
    IReadOnlySet<string> AmbiguousTemplateFields,
    IReadOnlySet<string> UnpinnedTemplateFields);

/// <summary>
/// Thrown when a rule body cannot be compiled into a single FetchXML filter. The message names the offending
/// clause/field so a policy author can fix the body; the compiler never emits a partial or guessed query.
/// </summary>
public sealed class PredicateCompilationException : Exception
{
    public PredicateCompilationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Compiles an <c>Existence</c> rule body (<c>sprk_policyversion.sprk_rulebody</c>, rule type
/// <see cref="RuleType.Existence"/>) into ONE Dataverse FetchXML query (spec FR-06, FR-07, NFR-07; design.md
/// §8.0.1; mvp-technical-spec.md §3.4a / §11.4).
/// </summary>
/// <remarks>
/// <para><b>Shape — one query, both conjuncts (CM-3).</b> Rooted at the body's <c>subject</c>, projecting only the
/// subject id, <c>distinct="true"</c> (an inner join to N qualifying related rows would otherwise repeat the
/// subject N times). Each clause becomes one <c>link-entity</c> joined <c>from</c> the clause's <c>path</c> (the
/// lookup on the related entity) <c>to</c> the subject id, aliased <c>c{index}</c>:</para>
/// <list type="bullet">
/// <item><b><c>exists</c></b> → <c>link-type="inner"</c> with the clause filter INSIDE the link (the
/// <c>LinkCriteria</c> shape of <c>DataversePrecedentBoard.cs:182-190</c>, the only in-repo EXISTS prior art).</item>
/// <item><b><c>notExists</c></b> → <c>link-type="outer"</c> with the clause filter INSIDE the link (so it lands in
/// the join's ON clause), plus a root-level <c>&lt;condition entityname="c{i}" attribute="{related}id"
/// operator="null"/&gt;</c>. This is the anti-join; there is no in-repo template for it (project CLAUDE.md §3.4).</item>
/// </list>
/// <para><b>Why the window MUST sit inside the outer link and not in the root filter.</b> Placed in the root
/// (WHERE) filter, the window condition is evaluated against the outer join's null row and is never true, so the
/// query returns NOTHING — which reads as a quiet, working predicate. Proven on real <c>spaarkedev1</c> data
/// 2026-10-04: the broken placement returned zero rows where the correct placement returned four
/// (<c>notes/pathb-fetchxml-reference.md</c> §3, probe V4). Do not "tidy" the window into the root filter.</para>
/// <para><b>No cross-clause variable passing (design.md §8.0.1(a)).</b> Every clause is compiled independently
/// from its own JSON; nothing one clause produces is visible to another. The task 020 schema already refuses a
/// <c>bind</c> property and any <c>$</c>-prefixed value; this compiler runs that same validator first and ALSO
/// refuses a <c>$</c>-prefixed string itself, so it cannot reintroduce the path even if the schema loosens.</para>
/// <para><b>Relative dates.</b> A string value <c>now</c> or <c>now-{N}d</c> resolves against
/// <see cref="TimeProvider"/> at compile time to an absolute UTC instant emitted with a <c>Z</c> suffix
/// (verified on <c>spaarkedev1</c> to be honored as UTC, not the caller's local zone). The body's operator is
/// kept as written: <c>{"&gt;=": "now-30d"}</c> compiles to <c>operator="ge"</c>. FetchXML's
/// <c>last-x-days</c> is deliberately NOT used — it truncates to the start of day in the calling user's time zone
/// and caps the range at "now", which is a different predicate from the one the author wrote. Any other string
/// shaped like a relative date but outside that grammar (<c>NOW-30d</c>, <c>now-30</c>, <c>now+1d</c>) is refused
/// rather than sent to Dataverse as a literal.</para>
/// <para><b>FR-07.</b> A clause or <c>when</c> filter that reads <c>sprk_budget.modifiedon</c> is refused at
/// compile time: any unrelated field edit bumps it, so it would read as "the budget was revised" and suppress a
/// true Signal — a false negative, the direction decision 13 rules against.</para>
/// <para><b>Operators.</b> scalar → <c>eq</c>; array → <c>in</c> (an empty array is refused — "in {}" is never a
/// meaningful rule); <c>{"op": v}</c> → <c>ge/le/gt/lt/ne/eq</c>. Note <c>ne</c> in Dataverse FetchXML INCLUDES
/// rows whose column is null (verified on <c>spaarkedev1</c>: <c>sprk_reviewoutcome ne Dismiss</c> returned the
/// unreviewed communications too), which is the recall-favouring reading of "not Dismiss" — so no extra
/// <c>or null</c> branch is emitted.</para>
/// <para><b>Joins fail closed.</b> A clause's <c>path</c> must be the one entry in <see cref="VerifiedJoins"/> for
/// its <c>(subject, clause entity)</c> pair — a lookup confirmed against the live schema to target the subject. A
/// wrong-but-valid column (e.g. <c>sprk_regardingbudget</c>) would never match and make a <c>notExists</c> vacuously
/// true. Clauses are capped at <see cref="MaxClauses"/> (Dataverse's link-entity limit); the subject key is emitted
/// as <c>&lt;order&gt;</c> so distinct results page consistently.</para>
/// <para><b>Read depth fails closed.</b> The subject and every clause entity must be in
/// <see cref="EvaluatorGlobalReadableEntities"/> — tables the evaluating principal reads org-wide. Over a table it
/// reads only at Basic depth, Dataverse's row trimming would make a <c>notExists</c> vacuously true for every
/// subject (task 006 finding, <c>notes/security-roles.md</c> §9).</para>
/// <para><b>Primary-key convention.</b> The subject id and the anti-join's null-tested column are
/// <c>{logicalname}id</c>, which holds for every custom (<c>sprk_</c>) table. It does NOT hold for activity
/// tables (<c>activityid</c>); no current subject or clause entity is one. Logical names are checked against
/// <c>^[a-z][a-z0-9_]*$</c> before reaching the query.</para>
/// <para><b>Scope of this class.</b> Pure, synchronous, no I/O: rule-body JSON in, FetchXML out. Running the
/// query, paging the result and writing Signals are the evaluator's job (tasks 030/031), not this one's.
/// Policy scope (tenant/matter applicability) is <see cref="PolicyScopeResolver"/>'s job and is not
/// re-derived here; the optional <c>subjectId</c> only narrows one evaluation to one subject (the per-matter
/// event triggers, spec FR-13). ADR-013: no AI-internal types.</para>
/// </remarks>
public sealed partial class PredicateCompiler
{
    private const string BudgetEntity = "sprk_budget";
    private const string ModifiedOnColumn = "modifiedon";

    /// <summary>
    /// The tables the evaluating principal (<c># mi-ontology-writer-dev</c>, systemuserid
    /// <c>3121bf1b-9fbf-f111-aaaf-0022482913fc</c>) reads at <b>Global</b> depth, per its effective privilege union
    /// verified 2026-10-04 (<c>projects/spaarke-ontology-platform-r1/notes/security-roles.md</c> §9). A rule may
    /// name only these as its subject or as a clause entity.
    /// </summary>
    /// <remarks>
    /// <b>Why this is a hard refusal, not a warning.</b> Dataverse trims a query to the rows the caller can read.
    /// Over a table read at Basic (own-rows) depth, a <c>notExists</c> clause sees an effectively empty table and is
    /// TRUE FOR EVERY SUBJECT — the evaluator would assert something it never checked (§0.3), producing Signals
    /// rather than an error. An <c>exists</c> clause fails the other way (silently false), and a non-Global subject
    /// silently drops subjects. Every one of those reads as a working predicate, so the compiler fails closed.
    /// <b>This list mirrors role configuration and can drift from it</b>: widening it is a code change with review,
    /// and the evaluator (task 030) is responsible for verifying the principal's live read depth at run time.
    /// </remarks>
    public static readonly FrozenSet<string> EvaluatorGlobalReadableEntities = new[]
    {
        "sprk_matter",
        "sprk_communication",
        "sprk_budget",
        "sprk_budgetrevision",
        "sprk_invoice",
        "sprk_project",
        "sprk_event",
        "sprk_memo",
        "sprk_todo",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The verified joins: for each <c>(subject, clause entity)</c> pair, the ONE lookup on the clause entity that
    /// targets the subject. Each entry was confirmed against the live <c>spaarkedev1</c> schema on 2026-10-04 (MCP
    /// <c>describe</c>: <c>sprk_communication.sprk_regardingmatter</c> → <c>sprk_matter</c>;
    /// <c>sprk_budgetrevision.sprk_matter</c> → <c>sprk_matter</c>).
    /// </summary>
    /// <remarks>
    /// <b>Why a closed list and not "any lookup".</b> Dataverse joins <c>from</c>/<c>to</c> on any type-compatible
    /// columns without requiring a relationship. A <c>path</c> naming the wrong lookup — e.g.
    /// <c>sprk_communication.sprk_regardingbudget</c>, which exists and holds budget ids — compares budget ids with
    /// matter ids, never matches, and makes a <c>notExists</c> TRUE FOR EVERY SUBJECT (an <c>exists</c> false for
    /// every subject). The query still looks valid. Adding a join is a schema check plus a code change with review.
    /// </remarks>
    public static readonly FrozenDictionary<(string Subject, string Related), string> VerifiedJoins =
        new Dictionary<(string Subject, string Related), string>
        {
            [("sprk_matter", "sprk_communication")] = "sprk_regardingmatter",
            [("sprk_matter", "sprk_budgetrevision")] = "sprk_matter",
        }.ToFrozenDictionary();

    /// <summary>Dataverse's limit on <c>link-entity</c> elements in one query. One clause = one link.</summary>
    public const int MaxClauses = 15;

    /// <summary>Task 022 review finding #9 (bounded refusal). An alias of
    /// <see cref="RuleBodySchemaValidator.MaxRuleBodyLength"/> — one number for every layer (rework round 2,
    /// finding F2, which also moved the check ahead of schema evaluation in every caller).</summary>
    public const int MaxRuleBodyLength = RuleBodySchemaValidator.MaxRuleBodyLength;

    /// <summary>Task 022 review finding #9 (bounded refusal): the shipped example's one <c>in</c> list holds 2
    /// values; 200 is far beyond any realistic authored rule while bounding both the resulting FetchXML
    /// string's size and Dataverse's own per-query cost for a large <c>in</c> condition.</summary>
    public const int MaxInListLength = 200;

    private static readonly JsonDocumentOptions StrictJson = new() { AllowDuplicateProperties = false };

    private readonly RuleBodySchemaValidator _validator;
    private readonly TimeProvider _timeProvider;

    public PredicateCompiler(RuleBodySchemaValidator validator, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _validator = validator;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Compiles <paramref name="ruleBodyJson"/> into a single FetchXML query.
    /// </summary>
    /// <param name="ruleBodyJson">An <c>Existence</c> rule body.</param>
    /// <param name="subjectId">Optional: restrict evaluation to one subject record (event-triggered runs).</param>
    /// <exception cref="PredicateCompilationException">The body is invalid or uses a construct that cannot be
    /// expressed as one FetchXML filter. Never returns a partial query.</exception>
    /// <remarks>
    /// <b>Who may call this.</b> Within <c>Sprk.Bff.Api</c>, only <see cref="PolicyVersionValidator"/> may call
    /// any <c>Compile*</c> method of this class (it uses <see cref="CompileSchemaValidated"/>, so the schema is
    /// evaluated once). Pinned by the architecture test <c>PredicateCompilerCallerGuardTests</c> (task 022
    /// rework round 2, finding F11): the evaluator (task 031) must obtain a predicate via
    /// <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/>, never here. Public for its own
    /// maintain-class tests (<c>PredicateCompilerTests</c>).
    /// </remarks>
    public CompiledPredicate Compile(string ruleBodyJson, Guid? subjectId = null)
    {
        using var doc = ParseBounded(ruleBodyJson);

        // Schema first (shape), then the compiler's own stricter checks.
        var validation = _validator.Validate(RuleType.Existence, ruleBodyJson);
        if (!validation.IsValid)
        {
            throw new PredicateCompilationException(
                "Rule body failed Existence schema validation: " + string.Join("; ", validation.Errors));
        }

        return CompileParsed(doc.RootElement, subjectId);
    }

    /// <summary>
    /// <see cref="Compile"/> minus the schema evaluation, for a caller that has ALREADY obtained
    /// <c>IsValid == true</c> from <see cref="RuleBodySchemaValidator.Validate"/> for this exact text — task 022
    /// rework round 2, finding F2: <see cref="PolicyVersionValidator"/> evaluated the schema and then
    /// <see cref="Compile"/> evaluated it again, doubling the time spent under the schema validator's
    /// process-wide lock. Internal; its only caller is <see cref="PolicyVersionValidator"/> (pinned by
    /// <c>PredicateCompilerCallerGuardTests</c>). The length cap, strict parse, and every compiler-level
    /// refusal (including the <c>$</c>-reference and numeric-range defences) still run.
    /// </summary>
    internal CompiledPredicate CompileSchemaValidated(string ruleBodyJson, Guid? subjectId)
    {
        using var doc = ParseBounded(ruleBodyJson);
        return CompileParsed(doc.RootElement, subjectId);
    }

    private static JsonDocument ParseBounded(string? ruleBodyJson)
    {
        // Bounded refusal BEFORE any parsing (review finding #9): reject a pathological body by length alone.
        if ((ruleBodyJson?.Length ?? 0) > MaxRuleBodyLength)
        {
            throw new PredicateCompilationException(RuleBodySchemaValidator.RuleBodyTooLongMessage(ruleBodyJson!.Length));
        }

        // Parse strictly: duplicate keys are refused here, so the schema validator and this compiler can never see
        // two different objects in the same text.
        try
        {
            return JsonDocument.Parse(ruleBodyJson ?? string.Empty, StrictJson);
        }
        catch (JsonException ex)
        {
            throw new PredicateCompilationException("Rule body is not valid JSON (duplicate keys are refused): " + ex.Message);
        }
    }

    private CompiledPredicate CompileParsed(JsonElement root, Guid? subjectId)
    {
        // Resolved once, so every clause in the query shares one anchor (to whole seconds, for a stable text).
        var nowUtc = TruncateToSeconds(_timeProvider.GetUtcNow().ToUniversalTime());

        var subject = RequireGlobalReadable(RequireIdentifier(root.GetProperty("subject").GetString(), "subject"), "subject");
        var subjectIdAttribute = subject + "id";

        var clauses = root.GetProperty("all");
        if (clauses.GetArrayLength() > MaxClauses)
        {
            throw new PredicateCompilationException(
                $"all: {clauses.GetArrayLength()} clauses exceed Dataverse's limit of {MaxClauses} link-entities per query.");
        }

        // <order> on the subject key: Microsoft's FetchXML paging guidance requires an order for distinct queries to
        // page consistently, and the evaluator (task 031) pages. Ordering does not change membership.
        var entity = new XElement("entity",
            new XAttribute("name", subject),
            new XElement("attribute", new XAttribute("name", subjectIdAttribute)),
            new XElement("order", new XAttribute("attribute", subjectIdAttribute)));

        var rootConditions = new List<XElement>();
        // Field name -> how it is read in POSITIVE positions ("when" / "exists"). NEVER populated for a
        // "notExists" clause: a firing subject has no matching row there, so its values are never readable.
        var positiveUses = new Dictionary<string, PositiveFieldUse>(StringComparer.Ordinal);

        if (subjectId is { } id)
        {
            if (id == Guid.Empty)
            {
                throw new PredicateCompilationException("subjectId must not be Guid.Empty.");
            }

            rootConditions.Add(Condition(subjectIdAttribute, "eq", id.ToString("D")));
        }

        if (root.TryGetProperty("when", out var when) && when.ValueKind == JsonValueKind.Object)
        {
            rootConditions.AddRange(CompileFilterConditions(subject, when, nowUtc, "when",
                (field, _) => UseOf(positiveUses, field).RecordWhen(subject)));
        }

        var index = 0;
        foreach (var clause in clauses.EnumerateArray())
        {
            var alias = "c" + index.ToString(CultureInfo.InvariantCulture);
            var where = $"all[{index}]";

            var isExists = clause.TryGetProperty("exists", out var existsEl);
            var relatedRaw = isExists ? existsEl.GetString() : clause.GetProperty("notExists").GetString();
            var related = RequireGlobalReadable(
                RequireIdentifier(relatedRaw, where + (isExists ? ".exists" : ".notExists")),
                where + (isExists ? ".exists" : ".notExists"));
            // Filter first, so an FR-07 violation is reported as FR-07 even where the join is also unverified.
            // Positive-field tracking is passed ONLY for an exists clause (review finding #5).
            var linkFilter = new XElement("filter", new XAttribute("type", "and"),
                CompileFilterConditions(related, clause.GetProperty("filter"), nowUtc, where + ".filter",
                    isExists ? (field, pin) => UseOf(positiveUses, field).RecordExists(related, pin) : null));

            var path = RequireVerifiedJoin(
                subject, related, RequireIdentifier(clause.GetProperty("path").GetString(), where + ".path"), where + ".path");

            entity.Add(new XElement("link-entity",
                new XAttribute("name", related),
                new XAttribute("from", path),
                new XAttribute("to", subjectIdAttribute),
                new XAttribute("link-type", isExists ? "inner" : "outer"),
                new XAttribute("alias", alias),
                linkFilter));

            if (!isExists)
            {
                // The anti-join: no related row survived the ON-clause filter for this subject.
                rootConditions.Add(new XElement("condition",
                    new XAttribute("entityname", alias),
                    new XAttribute("attribute", related + "id"),
                    new XAttribute("operator", "null")));
            }

            index++;
        }

        if (rootConditions.Count > 0)
        {
            entity.Add(new XElement("filter", new XAttribute("type", "and"), rootConditions));
        }

        var fetch = new XElement("fetch", new XAttribute("distinct", "true"), entity);

        string fetchXml;
        try
        {
            fetchXml = fetch.ToString(SaveOptions.None);
        }
        catch (ArgumentException ex)
        {
            // XML-invalid characters (e.g. U+0001) in a filter value. Fail closed with the compiler's own exception type.
            throw new PredicateCompilationException("Rule body contains a character that cannot appear in XML: " + ex.Message);
        }

        var eligible = new List<string>();
        var ambiguous = new List<string>();
        var unpinned = new List<string>();
        foreach (var (field, use) in positiveUses)
        {
            switch (use.Classify())
            {
                case TemplateFieldClass.Eligible: eligible.Add(field); break;
                case TemplateFieldClass.Ambiguous: ambiguous.Add(field); break;
                default: unpinned.Add(field); break;
            }
        }

        return new CompiledPredicate(
            SubjectEntity: subject,
            SubjectIdAttribute: subjectIdAttribute,
            FetchXml: fetchXml,
            WindowAnchorUtc: nowUtc,
            TemplateEligibleFields: eligible.ToFrozenSet(StringComparer.Ordinal),
            AmbiguousTemplateFields: ambiguous.ToFrozenSet(StringComparer.Ordinal),
            UnpinnedTemplateFields: unpinned.ToFrozenSet(StringComparer.Ordinal));
    }

    private static PositiveFieldUse UseOf(Dictionary<string, PositiveFieldUse> uses, string field)
    {
        if (!uses.TryGetValue(field, out var use))
        {
            use = new PositiveFieldUse();
            uses[field] = use;
        }

        return use;
    }

    private enum TemplateFieldClass
    {
        Eligible,
        Ambiguous,
        Unpinned,
    }

    /// <summary>
    /// How one field name is read in POSITIVE positions. <see cref="Classify"/> is the single definition of the
    /// §0.3 template vocabulary documented on <see cref="CompiledPredicate.TemplateEligibleFields"/> (task 022
    /// rework round 2, finding F3).
    /// </summary>
    private sealed class PositiveFieldUse
    {
        private readonly HashSet<string> _entities = new(StringComparer.Ordinal);
        private readonly List<string?> _existsPins = new();
        private bool _readByWhen;

        public void RecordWhen(string subjectEntity)
        {
            _readByWhen = true;
            _entities.Add(subjectEntity);
        }

        /// <param name="pin">The single value the clause pins the field to, or <c>null</c> if it does not.</param>
        public void RecordExists(string relatedEntity, string? pin)
        {
            _entities.Add(relatedEntity);
            _existsPins.Add(pin);
        }

        public TemplateFieldClass Classify()
        {
            // Two entities, or the subject's own row AND a related row (different rows even on the same table):
            // a flat {{field}} token cannot say which one it means.
            if (_entities.Count > 1 || (_readByWhen && _existsPins.Count > 0))
            {
                return TemplateFieldClass.Ambiguous;
            }

            if (_readByWhen)
            {
                // The subject is one row: its own value is defined whatever the operator.
                return TemplateFieldClass.Eligible;
            }

            if (_existsPins.Any(p => p is null))
            {
                // A range / <> / multi-value 'in' can match N rows carrying N values.
                return TemplateFieldClass.Unpinned;
            }

            return _existsPins.Distinct(StringComparer.Ordinal).Count() == 1
                ? TemplateFieldClass.Eligible
                : TemplateFieldClass.Ambiguous; // two clauses pinning DIFFERENT values
        }
    }

    /// <param name="recordPositive">Non-null ONLY when <paramref name="filter"/> is a POSITIVE filter (the body's
    /// own "when", or an "exists" clause); called once per field with the single value the condition pins the
    /// field to (a scalar, <c>{"=": v}</c>, or a one-element <c>in</c>), or <c>null</c> when it does not.</param>
    private static IEnumerable<XElement> CompileFilterConditions(
        string entityName, JsonElement filter, DateTimeOffset nowUtc, string where,
        Action<string, string?>? recordPositive)
    {
        var conditions = new List<XElement>();

        foreach (var field in filter.EnumerateObject())
        {
            var attribute = RequireIdentifier(field.Name, where);
            string? pin = null;

            var at = $"{where}.{field.Name}";

            if (string.Equals(entityName, BudgetEntity, StringComparison.Ordinal)
                && string.Equals(attribute, ModifiedOnColumn, StringComparison.Ordinal))
            {
                throw new PredicateCompilationException(
                    $"{at}: sprk_budget.modifiedon is not evidence of a budget revision (spec FR-07). Any " +
                    "unrelated edit bumps it and would suppress a true Signal; read sprk_budgetrevision.sprk_revisedon.");
            }

            var value = field.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.Array:
                    var values = value.EnumerateArray().Select(v => FormatScalar(v, nowUtc, at)).ToList();
                    if (values.Count == 0)
                    {
                        throw new PredicateCompilationException($"{at}: an empty id list matches nothing; refusing to compile.");
                    }

                    if (values.Count > MaxInListLength)
                    {
                        throw new PredicateCompilationException(
                            $"{at}: an 'in' list of {values.Count} values exceeds the {MaxInListLength}-value limit.");
                    }

                    conditions.Add(new XElement("condition",
                        new XAttribute("attribute", attribute),
                        new XAttribute("operator", "in"),
                        values.Select(v => new XElement("value", v))));
                    pin = values.Count == 1 ? values[0] : null; // 'in' of exactly one value pins the field
                    break;

                case JsonValueKind.Object:
                    var ops = value.EnumerateObject().ToList();
                    if (ops.Count != 1)
                    {
                        throw new PredicateCompilationException($"{at}: a comparison object must hold exactly one operator.");
                    }

                    var op = MapOperator(ops[0].Name, at);
                    var operand = FormatScalar(ops[0].Value, nowUtc, at);
                    conditions.Add(Condition(attribute, op, operand));
                    pin = op == "eq" ? operand : null; // only {"=": v} pins; ranges and <> do not
                    break;

                default:
                    var scalar = FormatScalar(value, nowUtc, at);
                    conditions.Add(Condition(attribute, "eq", scalar));
                    pin = scalar; // a bare scalar is eq
                    break;
            }

            recordPositive?.Invoke(attribute, pin);
        }

        return conditions;
    }

    private static XElement Condition(string attribute, string op, string value) =>
        new("condition",
            new XAttribute("attribute", attribute),
            new XAttribute("operator", op),
            new XAttribute("value", value));

    private static string MapOperator(string op, string at) => op switch
    {
        ">=" => "ge",
        "<=" => "le",
        ">" => "gt",
        "<" => "lt",
        "<>" => "ne",
        "=" => "eq",
        _ => throw new PredicateCompilationException($"{at}: unsupported operator '{op}'."),
    };

    private static string FormatScalar(JsonElement value, DateTimeOffset nowUtc, string at)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var s = value.GetString() ?? string.Empty;
                if (s.StartsWith('$'))
                {
                    // Defence in depth over the task 020 schema: a '$' value is a cross-clause variable reference,
                    // which a single FetchXML filter cannot express (design.md §8.0.1(a)).
                    throw new PredicateCompilationException(
                        $"{at}: '{s}' is a cross-clause variable reference; clauses are independent (design.md §8.0.1(a)).");
                }

                if (LooksLikeRelativeDate().IsMatch(s))
                {
                    var match = RelativeDateToken().Match(s);
                    if (!match.Success)
                    {
                        throw new PredicateCompilationException(
                            $"{at}: '{s}' is not a supported relative date. Use 'now' or 'now-{{N}}d'.");
                    }

                    var instant = match.Groups["days"].Success
                        ? nowUtc.AddDays(-int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture))
                        : nowUtc;
                    return instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                }

                return s;

            case JsonValueKind.Number:
                // Task 022 rework round 2, finding F7 (defence in depth over the schema, which now applies the
                // same per-value rules to 'when'): a number must be representable as System.Decimal -- the same
                // bound the schema library's numeric evaluation enforces (it reads values via GetDecimal). Without
                // this, "1e999999" reached FetchXML verbatim as value="1e999999".
                if (!value.TryGetDecimal(out _))
                {
                    throw new PredicateCompilationException(
                        $"{at}: numeric literal {value.GetRawText()} is outside the supported range (it must be " +
                        "representable as a System.Decimal).");
                }

                return value.GetRawText();

            case JsonValueKind.True:
                return "1";

            case JsonValueKind.False:
                return "0";

            default:
                throw new PredicateCompilationException($"{at}: unsupported value kind {value.ValueKind}.");
        }
    }

    private static string RequireIdentifier(string? name, string at)
    {
        if (string.IsNullOrEmpty(name) || !LogicalName().IsMatch(name))
        {
            throw new PredicateCompilationException(
                $"{at}: '{name}' is not a Dataverse logical name (expected ^[a-z][a-z0-9_]*$).");
        }

        return name;
    }

    private static string RequireGlobalReadable(string entity, string at)
    {
        if (!EvaluatorGlobalReadableEntities.Contains(entity))
        {
            // States only what was checked: membership of the verified list, not the principal's live privileges.
            throw new PredicateCompilationException(
                $"{at}: '{entity}' is not in the verified Global-read allow-list for the evaluating principal " +
                "(notes/security-roles.md §9); refusing to compile. Widening it is a role check plus a code change with review.");
        }

        return entity;
    }

    private static string RequireVerifiedJoin(string subject, string related, string path, string at)
    {
        if (!VerifiedJoins.TryGetValue((subject, related), out var verifiedPath)
            || !string.Equals(path, verifiedPath, StringComparison.Ordinal))
        {
            throw new PredicateCompilationException(
                $"{at}: '{related}.{path}' is not a verified lookup from {related} to {subject}. A wrong join column " +
                "never matches, which makes notExists true (and exists false) for every subject; refusing to compile." +
                (verifiedPath is null ? string.Empty : $" The verified path is '{verifiedPath}'."));
        }

        return path;
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    // All anchors are \z, never $: in .NET, $ also matches before a trailing '\n', so "modifiedon\n" would pass a $-anchored
    // name check and slip past the FR-07 string comparison.
    [GeneratedRegex(@"^now(?:-(?<days>[0-9]{1,4})d)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeDateToken();

    // Anything an author plausibly MEANT as a relative date ('now', 'NOW-30d', 'now-30', 'now+1d', ' now', 'now\n').
    // Such a value is either the supported grammar or refused — never sent to Dataverse as a literal string. A literal
    // in which 'now' is followed by a letter (e.g. a surname 'Nowak') is not matched and passes through.
    [GeneratedRegex(@"^\s*now(?![a-z]).*\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LooksLikeRelativeDate();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();
}
