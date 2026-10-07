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
/// <param name="WindowAnchorUtc">The instant every relative-date token (<c>now</c>, <c>now-30d</c>, <c>now+3d</c>) was resolved
/// against. Recorded so a caller can state, truthfully, which window a Signal was evaluated over (§0.3).</param>
/// <param name="TemplateEligibleFields">The field names a <c>sprk_messagetemplate</c> <c>{{token}}</c> may
/// reference — the ONE definition of the §0.3 template vocabulary (task 022 rework round 2, finding F3,
/// coordinator decision). A token's value must be exactly ONE well-defined value for the firing subject, so a
/// field is eligible only when:
/// <list type="bullet">
/// <item>it is read by the subject's own <c>when</c> filter — with any operator, because the subject is ONE row,
/// so the field has at most one value for it. That value is not guaranteed NON-NULL (task 022 rework round 3,
/// finding L4): Dataverse <c>ne</c> (from <c>&lt;&gt;</c>) includes rows whose column is null.
/// <see cref="SignalWriter.RenderSentence"/> refuses a null fact, so it surfaces as a refused write, never as
/// blank text; or</item>
/// <item>it is read by an <c>exists</c> clause that PINS it to exactly one value — a bare scalar, <c>{"=": v}</c>,
/// or an <c>in</c> list of exactly one element. A range/<c>&lt;&gt;</c>/multi-value <c>in</c> filter can match N
/// related rows carrying N different values, so "the" value would be undefined. For such a token the value is
/// the clause's pinned literal (see <c>SignalWriteRequest.FactValues</c> for the string-collation caveat). If the
/// field is read by more than one <c>exists</c> clause, EVERY occurrence must pin it and all pins must agree —
/// numbers by decimal value, strings ordinally (round 3, finding L3).</item>
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
/// <param name="QuietWindowDays">The rule body's <c>quietWindowDays</c> knob (D-13, spec FR-17a): the days after a
/// dismissal before the subject may re-raise. <see cref="PredicateCompiler.DefaultQuietWindowDays"/> (14) when the
/// body omits it. Carried, not applied: re-raise is the evaluator's job (task 031).</param>
/// <param name="DateOnlyWhenFields">The subject's <c>when</c> fields that are Date Only columns (in
/// <see cref="PredicateCompiler.DateOnlyColumns"/>), task 024. <b>The compiled query is EXACT at
/// <see cref="WindowAnchorUtc"/></b>: every relative date is resolved against that one instant, and the compiler
/// applies no time-zone logic. For these fields a "day" is a calendar date whose "today" depends on the item (D-25:
/// assignee's time zone, then owner's, then UTC), so the instant-anchored filter can both include and EXCLUDE an
/// item near a day boundary. Re-judging the returned rows can only drop false positives, never recover rows the
/// query left out: the evaluator (task 031) must therefore widen the query for these fields (for example anchor
/// at a day boundary and widen each Date Only bound by one day) before judging per item. Recorded as a 031 input
/// in <c>notes/024-progress.md</c>.</param>
public sealed record CompiledPredicate(
    string SubjectEntity,
    string SubjectIdAttribute,
    string FetchXml,
    DateTimeOffset WindowAnchorUtc,
    IReadOnlySet<string> TemplateEligibleFields,
    IReadOnlySet<string> AmbiguousTemplateFields,
    IReadOnlySet<string> UnpinnedTemplateFields,
    int QuietWindowDays,
    IReadOnlySet<string> DateOnlyWhenFields);

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
/// <para><b>Relative dates.</b> A string value <c>now</c>, <c>now-{N}d</c> or <c>now+{N}d</c> (task 024, D-16)
/// resolves against <see cref="TimeProvider"/> at compile time — ONE anchor per compile — to an absolute UTC instant
/// emitted with a <c>Z</c> suffix (verified on <c>spaarkedev1</c> to be honored as UTC, not the caller's local zone).
/// The body's operator is kept as written: <c>{"&gt;=": "now-30d"}</c> compiles to <c>operator="ge"</c>. FetchXML's
/// <c>last-x-days</c> / <c>next-x-days</c> are deliberately NOT used — they truncate to day boundaries in the
/// calling user's time zone (and <c>last-x-days</c> also caps the range at "now"), which is a different predicate
/// from the one the author wrote. Any other string shaped like a relative date but outside that grammar (<c>NOW-30d</c>, <c>now-30</c>,
/// <c>now+1h</c>, <c>now+-3d</c>) is refused rather than sent to Dataverse as a literal.</para>
/// <para><b>Two-bound date range (owner decision D-40, task 024).</b> A field may take
/// <c>{"&gt;=": "now", "&lt;=": "now+3d"}</c>: exactly one lower bound (<c>&gt;=</c>/<c>&gt;</c>) plus exactly one
/// upper bound (<c>&lt;=</c>/<c>&lt;</c>), both relative dates. It becomes two conditions in the SAME AND filter
/// — still one Dataverse filter, no OR, no join. A range that can match nothing (lower after upper, or equal bounds
/// with a strict operator) is refused, like an empty <c>in</c> list.</para>
/// <para><b>Subject-only bodies (task 024, D-16).</b> <c>all</c> may be empty when <c>when</c> holds at least one
/// condition: the query is the subject with its own filter and no <c>link-entity</c>. An empty <c>all</c> with no
/// <c>when</c> condition is refused — it would mean "every row of the subject".</para>
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
    /// <para><b><c>sprk_workassignment</c></b> was added by task 024 (D-16, a Do-lane subject) after the writer's read
    /// depth was confirmed live on 2026-10-07 with the §9.2 method: Global on the user side, rows identical as admin
    /// and as the writer (<c>notes/024-progress.md</c>). <c>sprk_servicerequest</c> stays out (not a D-16 subject).
    /// Being listed makes a table a valid SUBJECT; as a clause entity it still needs a <see cref="VerifiedJoins"/>
    /// entry, and none exists for the three Do-lane tables.</para>
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
        "sprk_workassignment",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Every <c>sprk_</c> column whose Dataverse <c>Format</c> is <c>DateOnly</c> on a table in
    /// <see cref="EvaluatorGlobalReadableEntities"/>, read from live <c>spaarkedev1</c> metadata on 2026-10-07
    /// (task 024). Feeds <see cref="CompiledPredicate.DateOnlyWhenFields"/> so the evaluator can apply per-item
    /// "today" (D-25) to a Date Only comparison.
    /// </summary>
    /// <remarks>
    /// The seam test <c>SignalPredicateTests.DateOnlyColumns_MatchTheLiveSchema_InBothDirections</c> fails if a
    /// listed column is not Date Only, or a Date Only column on a listed table is missing. A missing entry would make
    /// the evaluator treat a calendar date as an instant, which is off by a day near midnight; adding a table to the
    /// allow-list therefore means adding its Date Only columns here. System columns (<c>overriddencreatedon</c>) are
    /// out of scope. <c>sprk_todo.sprk_duedate</c> is Date Only in format but still <c>UserLocal</c> in behavior;
    /// a separate task (owner-approved 2026-10-07) converts it.
    /// </remarks>
    public static readonly FrozenSet<(string Entity, string Column)> DateOnlyColumns = new[]
    {
        ("sprk_matter", "sprk_closeddate"),
        ("sprk_matter", "sprk_lastreviewdate"),
        ("sprk_matter", "sprk_nextreviewdate"),
        ("sprk_matter", "sprk_openeddate"),
        ("sprk_budget", "sprk_budgetenddate"),
        ("sprk_budget", "sprk_budgetstartdate"),
        ("sprk_invoice", "sprk_invoicedate"),
        ("sprk_invoice", "sprk_invoiceduedate"),
        ("sprk_project", "sprk_closeddate"),
        ("sprk_project", "sprk_lastreviewdate"),
        ("sprk_project", "sprk_nextreviewdate"),
        ("sprk_project", "sprk_openeddate"),
        ("sprk_event", "sprk_approveddate"),
        ("sprk_event", "sprk_basedate"),
        ("sprk_event", "sprk_completeddate"),
        ("sprk_event", "sprk_duedate"),
        ("sprk_event", "sprk_finalduedate"),
        ("sprk_event", "sprk_meetingdate"),
        ("sprk_event", "sprk_tododuedate"),
        ("sprk_todo", "sprk_duedate"),
        ("sprk_workassignment", "sprk_responseduedate"),
    }.ToFrozenSet();

    /// <summary>The quiet window when a body omits <c>quietWindowDays</c> (D-13: 14 days).</summary>
    public const int DefaultQuietWindowDays = 14;

    /// <summary>Upper bound on <c>quietWindowDays</c> (ten years), matching the schema's <c>maximum</c>, so a
    /// date computed from it can never overflow.</summary>
    public const int MaxQuietWindowDays = 3650;

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
    /// evaluated once): the evaluator (task 031) must obtain a predicate via
    /// <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/>. Public for its own maintain-class tests
    /// (<c>PredicateCompilerTests</c>). See <see cref="PolicyVersionValidator"/>'s remarks for exactly what the
    /// architecture test <c>PredicateCompilerCallerGuardTests</c> enforces, and what it does not.
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
        catch (InvalidOperationException)
        {
            // Round 3, finding M1: with AllowDuplicateProperties=false, Parse itself unescapes every property name
            // to compare them, and an unpaired UTF-16 surrogate escape there throws InvalidOperationException.
            throw new PredicateCompilationException(UnreadableTextMessage);
        }
    }

    private const string UnreadableTextMessage =
        "Rule body is not valid JSON: it contains text that cannot be read as a string (for example an unpaired " +
        "UTF-16 surrogate escape such as \\ud800).";

    private CompiledPredicate CompileParsed(JsonElement root, Guid? subjectId)
    {
        try
        {
            return CompileParsedCore(root, subjectId);
        }
        catch (InvalidOperationException)
        {
            // Task 022 rework round 3, finding M1: an unpaired UTF-16 surrogate escape in a STRING VALUE passes the
            // strict parse (only property names are unescaped there -- see ParseBounded), but READING it
            // (GetString) throws InvalidOperationException. Reachable through CompileSchemaValidated, which skips
            // the schema validator that would otherwise refuse it first. Refused with the compiler's own exception
            // type, so Compile/CompileSchemaValidated keep their single documented failure contract.
            throw new PredicateCompilationException(UnreadableTextMessage);
        }
    }

    private CompiledPredicate CompileParsedCore(JsonElement root, Guid? subjectId)
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

        var dateOnlyWhenFields = new List<string>();
        var whenConditionCount = 0;
        if (root.TryGetProperty("when", out var when) && when.ValueKind == JsonValueKind.Object)
        {
            var whenConditions = CompileFilterConditions(subject, when, nowUtc, "when",
                (field, _) =>
                {
                    UseOf(positiveUses, field).RecordWhen(subject);
                    if (DateOnlyColumns.Contains((subject, field)))
                    {
                        dateOnlyWhenFields.Add(field);
                    }
                }).ToList();
            whenConditionCount = whenConditions.Count;
            rootConditions.AddRange(whenConditions);
        }

        if (clauses.GetArrayLength() == 0 && whenConditionCount == 0)
        {
            // Task 024 (D-16): a subject-only rule is a 'when' filter with zero clauses. With neither, the query
            // would return every row of the subject -- a Signal per row. Defence in depth over the schema's if/then.
            throw new PredicateCompilationException(
                "all: a body with no exists/notExists clause must scope its subject with at least one 'when' " +
                "condition; otherwise it matches every row of the subject.");
        }

        var quietWindowDays = ReadQuietWindowDays(root);

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
            UnpinnedTemplateFields: unpinned.ToFrozenSet(StringComparer.Ordinal),
            QuietWindowDays: quietWindowDays,
            DateOnlyWhenFields: dateOnlyWhenFields.ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>The optional <c>quietWindowDays</c> knob (D-13, FR-17a): absent → <see cref="DefaultQuietWindowDays"/>;
    /// otherwise an integer in 0..<see cref="MaxQuietWindowDays"/>. Re-checked here because
    /// <see cref="CompileSchemaValidated"/> skips the schema.</summary>
    private static int ReadQuietWindowDays(JsonElement root)
    {
        if (!root.TryGetProperty("quietWindowDays", out var knob))
        {
            return DefaultQuietWindowDays;
        }

        // TryGetDecimal, not TryGetInt32 (review F2): JSON Schema 'integer' accepts an integral number written as
        // 7.0 or 1e2, so the compiler must read those as 7 and 100 rather than refuse them with an untrue message.
        if (knob.ValueKind != JsonValueKind.Number || !knob.TryGetDecimal(out var value)
            || value != decimal.Truncate(value) || value < 0 || value > MaxQuietWindowDays)
        {
            throw new PredicateCompilationException(
                $"quietWindowDays: {knob.GetRawText()} is not a whole number of days between 0 and {MaxQuietWindowDays}.");
        }

        return (int)value;
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
        private readonly List<object?> _existsPins = new();
        private bool _readByWhen;

        public void RecordWhen(string subjectEntity)
        {
            _readByWhen = true;
            _entities.Add(subjectEntity);
        }

        /// <param name="pin">The single value the clause pins the field to (see <see cref="PinOf"/>), or
        /// <c>null</c> if it does not.</param>
        public void RecordExists(string relatedEntity, object? pin)
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
                // The subject is ONE row, so the field has at most one value for it -- but not necessarily a
                // non-null one: Dataverse 'ne' (from '<>') INCLUDES rows whose column is null (task 022 rework
                // round 3, finding L4), and an unfiltered-by-value operator says nothing about nullness either.
                // SignalWriter.RenderSentence refuses a null fact, so a null here surfaces as a refused write, never
                // as blank text; task 031 must expect it.
                return TemplateFieldClass.Eligible;
            }

            if (_existsPins.Any(p => p is null))
            {
                // A range / <> / multi-value 'in' can match N rows carrying N values.
                return TemplateFieldClass.Unpinned;
            }

            // Pins compare by VALUE (finding L3): numbers as System.Decimal (1 == 1.0 == 1e0), everything else as
            // the ordinal string the condition was emitted with.
            return _existsPins.Distinct().Count() == 1
                ? TemplateFieldClass.Eligible
                : TemplateFieldClass.Ambiguous; // two clauses pinning DIFFERENT values
        }
    }

    /// <summary>
    /// The comparable identity of a pinned value (task 022 rework round 3, finding L3): a JSON number is its
    /// <see cref="decimal"/> value (boxed — <see cref="decimal.Equals(object)"/> is value-based, so 1 and 1.0
    /// compare equal); anything else is the exact string emitted into FetchXML. <b>Strings stay ordinal</b>, which
    /// is conservative: Dataverse string <c>eq</c> is case- and accent-insensitive under the org collation, so
    /// <c>"Fee"</c> and <c>"fee"</c> pinned by two clauses would match the same rows yet are refused here as
    /// ambiguous rather than guessed equal.
    /// </summary>
    private static object PinOf(JsonElement value, string emitted) =>
        value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : emitted;

    /// <param name="recordPositive">Non-null ONLY when <paramref name="filter"/> is a POSITIVE filter (the body's
    /// own "when", or an "exists" clause); called once per field with the single value the condition pins the
    /// field to (a scalar, <c>{"=": v}</c>, or a one-element <c>in</c>; see <see cref="PinOf"/>), or <c>null</c>
    /// when it does not.</param>
    private static IEnumerable<XElement> CompileFilterConditions(
        string entityName, JsonElement filter, DateTimeOffset nowUtc, string where,
        Action<string, object?>? recordPositive)
    {
        var conditions = new List<XElement>();

        foreach (var field in filter.EnumerateObject())
        {
            var attribute = RequireIdentifier(field.Name, where);
            object? pin = null;

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
                    var elements = value.EnumerateArray().ToList();
                    var values = elements.Select(v => FormatScalar(v, nowUtc, at)).ToList();
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
                    pin = values.Count == 1 ? PinOf(elements[0], values[0]) : null; // one-value 'in' pins the field
                    break;

                case JsonValueKind.Object:
                    var ops = value.EnumerateObject().ToList();
                    if (ops.Count == 2)
                    {
                        // D-40: a two-bound relative-date range. Never pins (it can match many values).
                        conditions.AddRange(CompileDateRange(attribute, ops, nowUtc, at));
                        break;
                    }

                    if (ops.Count != 1)
                    {
                        throw new PredicateCompilationException(
                            $"{at}: a comparison object must hold exactly one operator, or one lower plus one upper " +
                            "relative-date bound (D-40).");
                    }

                    var op = MapOperator(ops[0].Name, at);
                    var operand = FormatScalar(ops[0].Value, nowUtc, at);
                    conditions.Add(Condition(attribute, op, operand));
                    pin = op == "eq" ? PinOf(ops[0].Value, operand) : null; // only {"=": v} pins
                    break;

                default:
                    var scalar = FormatScalar(value, nowUtc, at);
                    conditions.Add(Condition(attribute, "eq", scalar));
                    pin = PinOf(value, scalar); // a bare scalar is eq
                    break;
            }

            recordPositive?.Invoke(attribute, pin);
        }

        return conditions;
    }

    /// <summary>
    /// Owner decision D-40 (task 024): <c>{"&gt;=": "now", "&lt;=": "now+3d"}</c> — exactly one lower bound
    /// (<c>&gt;=</c>/<c>&gt;</c>) and one upper bound (<c>&lt;=</c>/<c>&lt;</c>), both relative dates resolved against
    /// the compile's one anchor. Emitted as two conditions in the caller's AND filter: one Dataverse filter, no OR.
    /// </summary>
    private static IEnumerable<XElement> CompileDateRange(
        string attribute, List<JsonProperty> ops, DateTimeOffset nowUtc, string at)
    {
        (string Op, DateTimeOffset Instant)? lower = null;
        (string Op, DateTimeOffset Instant)? upper = null;

        foreach (var bound in ops)
        {
            var op = bound.Name switch
            {
                ">=" => "ge",
                ">" => "gt",
                "<=" => "le",
                "<" => "lt",
                _ => throw new PredicateCompilationException(
                    $"{at}: '{bound.Name}' cannot bound a date range; use one of >= / > plus one of <= / < (D-40)."),
            };

            if (bound.Value.ValueKind != JsonValueKind.String
                || !TryResolveRelativeDate(bound.Value.GetString(), nowUtc, out var instant))
            {
                throw new PredicateCompilationException(
                    $"{at}: a two-bound range takes relative-date bounds only ('now', 'now-{{N}}d', 'now+{{N}}d'); " +
                    $"'{bound.Name}' has {bound.Value.GetRawText()} (D-40).");
            }

            var isLower = op is "ge" or "gt";
            if ((isLower ? lower : upper) is not null)
            {
                throw new PredicateCompilationException(
                    $"{at}: a date range takes exactly ONE lower bound (>= or >) and ONE upper bound (<= or <) (D-40).");
            }

            if (isLower) lower = (op, instant);
            else upper = (op, instant);
        }

        var (lowerOp, from) = lower!.Value;
        var (upperOp, to) = upper!.Value;
        if (from > to || (from == to && (lowerOp == "gt" || upperOp == "lt")))
        {
            // Like an empty 'in' list: a range no instant satisfies is never a meaningful rule.
            throw new PredicateCompilationException($"{at}: the range's lower bound is not before its upper bound, so it matches nothing.");
        }

        return new[] { Condition(attribute, lowerOp, FormatInstant(from)), Condition(attribute, upperOp, FormatInstant(to)) };
    }

    private static string FormatInstant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary><c>now</c> / <c>now-{N}d</c> / <c>now+{N}d</c> → the instant against <paramref name="nowUtc"/>.</summary>
    private static bool TryResolveRelativeDate(string? token, DateTimeOffset nowUtc, out DateTimeOffset instant)
    {
        var match = RelativeDateToken().Match(token ?? string.Empty);
        if (!match.Success)
        {
            instant = default;
            return false;
        }

        var days = match.Groups["days"].Success ? int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture) : 0;
        instant = nowUtc.AddDays(match.Groups["sign"].Value == "+" ? days : -days);
        return true;
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
                    if (!TryResolveRelativeDate(s, nowUtc, out var instant))
                    {
                        throw new PredicateCompilationException(
                            $"{at}: '{s}' is not a supported relative date. Use 'now', 'now-{{N}}d' or 'now+{{N}}d'.");
                    }

                    return FormatInstant(instant);
                }

                return s;

            case JsonValueKind.Number:
                // Task 022 rework round 2, finding F7 (defence in depth over the schema, which now applies the
                // same per-value rules to 'when'): a number must be representable as System.Decimal -- the same
                // bound the schema library's numeric evaluation enforces (it reads values via GetDecimal). Without
                // this, "1e999999" reached FetchXML verbatim as value="1e999999".
                if (!value.TryGetDecimal(out var number))
                {
                    throw new PredicateCompilationException(
                        $"{at}: numeric literal {value.GetRawText()} is outside the supported range (it must be " +
                        "representable as a System.Decimal).");
                }

                // Round 3, finding L3: emitted CANONICALLY (plain invariant decimal), never the raw JSON text --
                // "1e2" becomes "100" and "1E-3" becomes "0.001", so FetchXML never carries exponent notation.
                return number.ToString(CultureInfo.InvariantCulture);

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
    [GeneratedRegex(@"^now(?:(?<sign>[-+])(?<days>[0-9]{1,4})d)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeDateToken();

    // Anything an author plausibly MEANT as a relative date ('now', 'NOW-30d', 'now-30', 'now+1d', ' now', 'now\n').
    // Such a value is either the supported grammar or refused — never sent to Dataverse as a literal string. A literal
    // in which 'now' is followed by a letter (e.g. a surname 'Nowak') is not matched and passes through.
    [GeneratedRegex(@"^\s*now(?![a-z]).*\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LooksLikeRelativeDate();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();
}
