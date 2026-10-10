using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>One enabled policy and its current version, as the evaluator needs them (task 031).</summary>
/// <param name="Lane"><see cref="SignalWriter.LaneDecide"/> or <see cref="SignalWriter.LaneDo"/> (<c>sprk_policy.sprk_lane</c>).</param>
/// <param name="RuleType"><c>sprk_policyversion.sprk_ruletype</c>, by name or option value.</param>
public sealed record SignalEvaluationPolicy(
    Guid PolicyId,
    string PolicyCode,
    int Lane,
    Guid PolicyVersionId,
    string? RuleType,
    string? RuleBody,
    string? MessageTemplate);

/// <summary>
/// One subject the policy's predicate holds for (task 031). <see cref="Core"/> is the core record the writer would group it
/// under; when the writer refuses to derive one, <see cref="Core"/> is null and <see cref="CoreRefusalReason"/> carries the
/// writer's bounded reason (D-38: a refusal, never a skip).
/// </summary>
/// <param name="DueDate">The subject's date the Signal carries (D-27), or null for a subject without one.</param>
/// <param name="FactValues">The detection-time facts the predicate read for this subject (FR-03 snapshot, §0.3).</param>
/// <param name="Evidence">One witness line per <c>exists</c> clause (D-23, FR-48), when a witness row was found.</param>
public sealed record SignalHit(
    string SubjectEntity,
    Guid SubjectId,
    string? DisplayName,
    string? RecordNumber,
    SignalCoreRecord? Core,
    string? CoreRefusalReason,
    DateTime? DueDate,
    IReadOnlyDictionary<string, object?> FactValues,
    IReadOnlyList<SignalEvidenceRef> Evidence);

/// <summary>The result of <see cref="SignalEvaluator.EvaluateAsync"/>. <see cref="PolicyValid"/> false means the version
/// failed the evaluation-time gate (already logged and metered by <see cref="PolicyVersionValidator"/>): no hits, and the
/// caller must not close anything on the strength of it.</summary>
public sealed record SignalEvaluation(
    bool PolicyValid,
    string? SubjectEntity,
    DateTimeOffset WindowAnchorUtc,
    int QuietWindowDays,
    IReadOnlyList<SignalHit> Hits)
{
    internal static SignalEvaluation Invalid { get; } =
        new(false, null, default, PredicateCompiler.DefaultQuietWindowDays, Array.Empty<SignalHit>());
}

/// <summary>
/// The side-effect-free evaluation core (task 031; reconciliation B.2 row 031, M-7): <c>Evaluate(version, scope)</c> returns,
/// per subject the predicate holds for, the hit, its core record, its witness evidence and its facts. It WRITES NOTHING and
/// takes no lock: every read goes through the shared app-only <see cref="IGenericEntityService"/>, and the core record comes
/// from <see cref="SignalWriter.DeriveCoreRecordAsync"/>, which only reads. The nightly job (<see cref="SignalReevaluationJob"/>)
/// wraps it with the writes; the event triggers (task 032) and the admin dry run (task 101) reuse it as is.
/// </summary>
/// <remarks>
/// <para><b>The gate.</b> The compiled predicate comes only from <see cref="PolicyVersionValidator.TryPrepareForEvaluation"/>
/// (task 022 F6). The compiled query is copied as TEXT and widened; the <see cref="CompiledPredicate"/> itself is never
/// cloned (<c>PredicateCompilerCallerGuardTests</c>).</para>
/// <para><b>Per-item today (D-25, FR-47).</b> For a <c>when</c> field that is a Date Only column
/// (<see cref="CompiledPredicate.DateOnlyWhenFields"/>), a relative bound (<c>now</c>, <c>now-Nd</c>) means a calendar day in
/// the item's own zone: the assignee's, else the owner's, else UTC, through the shared <see cref="DataverseRecipientDays"/>
/// (task 098). The compiled bound is instant-anchored, so it can both include and exclude an item near midnight; the query
/// is therefore WIDENED by two days on each such bound and every returned row is re-judged against its own today. A row
/// whose judgement is the same for every possible today needs no time-zone lookup.</para>
/// <para><b>Facts (§0.3).</b> A <c>when</c> field's fact is the subject's own value; an <c>exists</c> field's fact is the
/// clause's pinned literal (the one value it fixes the field to). A <c>notExists</c> field is never a fact.</para>
/// <para><b>Witnesses (D-23, FR-48).</b> For each <c>exists</c> clause, the matching related row with the latest date is the
/// witness: kind, ref, the row's display text and source, its date as <c>asOf</c>, and the clause id <c>all[N]</c> (task
/// 026's convention). Witness values go to <c>sprk_evidencerefs</c> only, never into the sentence.</para>
/// <para>§11: <i>Existing</i> — <see cref="PolicyVersionValidator"/> compiles, <see cref="SignalWriter"/> writes one Signal;
/// nothing runs a compiled predicate. <i>Extension</i> — the writer is the write side and must stay free of query logic, and
/// the validator must stay side-effect free of Dataverse reads; the admin dry run (101) and event triggers (032) need this
/// core without the writes. <i>Cost of doing nothing</i> — no Signal is ever raised or closed (FR-11, FR-15).</para>
/// </remarks>
public sealed class SignalEvaluator
{
    /// <summary>FetchXML page size for the subject query and the witness queries.</summary>
    internal const int PageSize = 500;

    /// <summary>Pages read before the evaluation stops. Reaching it is the POML's escalation trigger (a full pass too
    /// expensive): the policy fails loudly rather than being silently narrowed.</summary>
    internal const int MaxPages = 200;

    /// <summary>Days each Date Only relative bound is widened by before the per-item judgement (zones span UTC-12..UTC+14).</summary>
    internal const int DateOnlyWideningDays = 2;

    /// <summary>The contact column naming who an item is FOR (D-25), on the Do-lane subjects that have one (verified live,
    /// task 031: To Do, event and work assignment all carry <c>sprk_assignedto</c>).</summary>
    internal static readonly FrozenDictionary<string, string> AssigneeColumnBySubject =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sprk_todo"] = "sprk_assignedto",
            ["sprk_event"] = "sprk_assignedto",
            ["sprk_workassignment"] = "sprk_assignedto",
        }.ToFrozenDictionary();

    /// <summary>The witness row's display text and source columns per related table (FR-48). A table not listed gives a
    /// witness line with no text.</summary>
    internal static readonly FrozenDictionary<string, (string? Text, string? Source)> WitnessColumnsByEntity =
        new Dictionary<string, (string? Text, string? Source)>(StringComparer.Ordinal)
        {
            ["sprk_communication"] = ("sprk_subject", "sprk_from"),
            ["sprk_budgetrevision"] = ("sprk_name", null),
        }.ToFrozenDictionary();

    private readonly PolicyVersionValidator _validator;
    private readonly IGenericEntityService _entities;
    private readonly SignalWriter _writer;
    private readonly ILogger<SignalEvaluator> _logger;

    public SignalEvaluator(
        PolicyVersionValidator validator, IGenericEntityService entities, SignalWriter writer, ILogger<SignalEvaluator> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Evaluates <paramref name="policy"/>'s current version over every subject (<paramref name="subjectId"/> null) or one.
    /// Writes nothing. A Dataverse read failure propagates: the caller must treat the policy's truth as unknown this run.
    /// </summary>
    public async Task<SignalEvaluation> EvaluateAsync(SignalEvaluationPolicy policy, Guid? subjectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var snapshot = new PolicyVersionSnapshot(
            policy.PolicyVersionId, policy.PolicyCode, policy.RuleType, policy.RuleBody, policy.MessageTemplate);
        if (!_validator.TryPrepareForEvaluation(snapshot, subjectId, out var compiled))
        {
            return SignalEvaluation.Invalid;
        }

        using var body = PredicateCompiler.ParseBounded(policy.RuleBody);
        var root = body.RootElement;
        var anchor = compiled.WindowAnchorUtc;
        var subject = compiled.SubjectEntity;

        var whenFields = root.TryGetProperty("when", out var when) && when.ValueKind == JsonValueKind.Object
            ? when.EnumerateObject().Select(p => p.Name).ToList()
            : new List<string>();
        var dateBounds = ReadDateOnlyBounds(when, compiled.DateOnlyWhenFields);
        var catalog = await ReadCatalogFieldsAsync(subject, ct).ConfigureAwait(false);
        SignalWriter.DueDateColumnBySubject.TryGetValue(subject, out var dueColumn);
        var assigneeColumn = dateBounds.Count > 0 && AssigneeColumnBySubject.TryGetValue(subject, out var a) ? a : null;

        var columns = new List<string>(whenFields);
        if (dueColumn is not null) columns.Add(dueColumn);
        if (catalog.DisplayField is not null) columns.Add(catalog.DisplayField);
        if (catalog.NumberField is not null) columns.Add(catalog.NumberField);
        if (dateBounds.Count > 0)
        {
            columns.Add("ownerid");
            if (assigneeColumn is not null) columns.Add(assigneeColumn);
        }

        var fetch = XDocument.Parse(compiled.FetchXml);
        var entity = fetch.Root!.Element("entity")!;
        WidenDateOnlyBounds(entity, dateBounds, anchor);
        foreach (var column in columns.Distinct(StringComparer.Ordinal).Where(c => c != compiled.SubjectIdAttribute))
        {
            entity.AddFirst(new XElement("attribute", new XAttribute("name", column)));
        }

        var rows = await RetrieveAllAsync(fetch, ct).ConfigureAwait(false);

        var recipientDays = dateBounds.Count > 0 ? new DataverseRecipientDays(_entities, anchor) : null;
        var candidates = new List<Entity>();
        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var id = row.GetAttributeValue<Guid?>(compiled.SubjectIdAttribute) ?? row.Id;
            if (id == Guid.Empty || !seen.Add(id))
            {
                continue;
            }

            row.Id = id;
            if (recipientDays is null || await HoldsForRecipientAsync(row, dateBounds, assigneeColumn, recipientDays, ct).ConfigureAwait(false))
            {
                candidates.Add(row);
            }
        }

        var clauses = ReadClauses(root);
        var witnesses = await ReadWitnessesAsync(entity, clauses, candidates.Select(r => r.Id).ToList(), anchor, ct).ConfigureAwait(false);
        var pinnedFacts = ReadPinnedFacts(clauses, anchor);

        var hits = new List<SignalHit>(candidates.Count);
        foreach (var row in candidates)
        {
            SignalCoreRecord? core = null;
            string? refusal = null;
            try
            {
                core = await _writer.DeriveCoreRecordAsync(subject, row.Id, policy.Lane, ct).ConfigureAwait(false);
            }
            catch (SignalWriterEscalationException ex)
            {
                refusal = ex.Reason;
                _logger.LogDebug(
                    "Policy {PolicyCode}: the core record of {SubjectEntity} {SubjectId} could not be derived (reason={Reason}).",
                    policy.PolicyCode, subject, row.Id, ex.Reason);
            }

            var facts = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in whenFields)
            {
                facts[field] = NormalizeValue(row.Attributes.TryGetValue(field, out var v) ? v : null);
            }

            foreach (var (field, value) in pinnedFacts)
            {
                facts.TryAdd(field, value);
            }

            hits.Add(new SignalHit(
                SubjectEntity: subject,
                SubjectId: row.Id,
                DisplayName: catalog.DisplayField is null ? null : TextOf(row, catalog.DisplayField),
                RecordNumber: catalog.NumberField is null ? null : TextOf(row, catalog.NumberField),
                Core: core,
                CoreRefusalReason: refusal,
                DueDate: dueColumn is null ? null : row.GetAttributeValue<DateTime?>(dueColumn)?.Date,
                FactValues: facts,
                Evidence: witnesses.TryGetValue(row.Id, out var lines) ? lines : Array.Empty<SignalEvidenceRef>()));
        }

        return new SignalEvaluation(true, subject, anchor, compiled.QuietWindowDays, hits);
    }

    // ── Date Only bounds (D-25) ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>One relative bound on a Date Only <c>when</c> field: the FetchXML operator and the day offset from today.
    /// <c>in</c> carries several offsets.</summary>
    internal sealed record DateOnlyBound(string Field, string Operator, IReadOnlyList<int> Days);

    internal static List<DateOnlyBound> ReadDateOnlyBounds(JsonElement when, IReadOnlySet<string> dateOnlyFields)
    {
        var bounds = new List<DateOnlyBound>();
        if (when.ValueKind != JsonValueKind.Object || dateOnlyFields.Count == 0)
        {
            return bounds;
        }

        foreach (var field in when.EnumerateObject().Where(p => dateOnlyFields.Contains(p.Name)))
        {
            var value = field.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.String when PredicateCompiler.TryParseRelativeDays(value.GetString(), out var days):
                    bounds.Add(new DateOnlyBound(field.Name, "eq", new[] { days }));
                    break;
                case JsonValueKind.Array:
                    var offsets = new List<int>();
                    foreach (var element in value.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String && PredicateCompiler.TryParseRelativeDays(element.GetString(), out var d))
                        {
                            offsets.Add(d);
                        }
                    }

                    // A list mixing relative and literal dates keeps its compiled condition as is (it is exact for the
                    // literal values); only an all-relative list is widened and re-judged.
                    if (offsets.Count > 0 && offsets.Count == value.GetArrayLength())
                    {
                        bounds.Add(new DateOnlyBound(field.Name, "in", offsets));
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var op in value.EnumerateObject())
                    {
                        if (op.Value.ValueKind == JsonValueKind.String && PredicateCompiler.TryParseRelativeDays(op.Value.GetString(), out var od))
                        {
                            var mapped = op.Name switch
                            {
                                ">=" => "ge", ">" => "gt", "<=" => "le", "<" => "lt", "<>" => "ne", "=" => "eq", _ => null,
                            };
                            if (mapped is not null)
                            {
                                bounds.Add(new DateOnlyBound(field.Name, mapped, new[] { od }));
                            }
                        }
                    }

                    break;
            }
        }

        return bounds;
    }

    /// <summary>
    /// Replaces each relative Date Only condition in the subject's root filter with a condition widened by
    /// <see cref="DateOnlyWideningDays"/> (an <c>ne</c> bound is dropped: every row is re-judged), so the query returns a
    /// superset that the per-item judgement narrows. Conditions on the same field with a literal date are left as compiled.
    /// </summary>
    internal static void WidenDateOnlyBounds(XElement entity, IReadOnlyList<DateOnlyBound> bounds, DateTimeOffset anchor)
    {
        if (bounds.Count == 0)
        {
            return;
        }

        var rootFilter = entity.Elements("filter").FirstOrDefault()
            ?? throw new InvalidOperationException("The compiled predicate has Date Only bounds but no root filter.");
        var widen = TimeSpan.FromDays(DateOnlyWideningDays);

        foreach (var bound in bounds)
        {
            var instants = bound.Days.Select(d => PredicateCompiler.FormatInstantForFetch(anchor.AddDays(d))).ToList();
            var condition = rootFilter.Elements("condition").FirstOrDefault(c =>
                c.Attribute("entityname") is null
                && (string?)c.Attribute("attribute") == bound.Field
                && (string?)c.Attribute("operator") == bound.Operator
                && (bound.Operator == "in"
                    ? c.Elements("value").Select(v => v.Value).SequenceEqual(instants)
                    : (string?)c.Attribute("value") == instants[0]))
                ?? throw new InvalidOperationException(
                    $"The compiled predicate has no '{bound.Operator}' condition on Date Only field '{bound.Field}' at the expected " +
                    "anchor, so it cannot be widened for the per-item judgement (D-25). Refusing to evaluate on an exact-instant bound.");
            condition.Remove();

            var lowest = anchor.AddDays(bound.Days.Min()) - widen;
            var highest = anchor.AddDays(bound.Days.Max()) + widen;
            if (bound.Operator is "eq" or "in" or "ge" or "gt")
            {
                rootFilter.Add(Condition(bound.Field, "ge", PredicateCompiler.FormatInstantForFetch(lowest)));
            }

            if (bound.Operator is "eq" or "in" or "le" or "lt")
            {
                rootFilter.Add(Condition(bound.Field, "le", PredicateCompiler.FormatInstantForFetch(highest)));
            }
        }
    }

    /// <summary>True when every Date Only bound holds for <paramref name="row"/> against ITS today (D-25).</summary>
    private static async Task<bool> HoldsForRecipientAsync(
        Entity row, IReadOnlyList<DateOnlyBound> bounds, string? assigneeColumn, DataverseRecipientDays days, CancellationToken ct)
    {
        var verdicts = days.PossibleTodays.Select(today => Holds(row, bounds, today)).Distinct().ToList();
        if (verdicts.Count == 1)
        {
            return verdicts[0];
        }

        var assignee = assigneeColumn is null ? null : row.GetAttributeValue<EntityReference>(assigneeColumn)?.Id;
        var recipient = await days.ForAsync(assignee, row.GetAttributeValue<EntityReference>("ownerid"), ct).ConfigureAwait(false);
        return Holds(row, bounds, recipient.Today);
    }

    internal static bool Holds(Entity row, IReadOnlyList<DateOnlyBound> bounds, DateOnly today)
    {
        foreach (var bound in bounds)
        {
            var raw = row.GetAttributeValue<DateTime?>(bound.Field);
            if (raw is not { } value)
            {
                // Dataverse 'ne' matches a null column; every other operator does not.
                if (bound.Operator != "ne") return false;
                continue;
            }

            var date = DateOnly.FromDateTime(value);
            var holds = bound.Operator switch
            {
                "eq" => date == today.AddDays(bound.Days[0]),
                "ne" => date != today.AddDays(bound.Days[0]),
                "lt" => date < today.AddDays(bound.Days[0]),
                "le" => date <= today.AddDays(bound.Days[0]),
                "gt" => date > today.AddDays(bound.Days[0]),
                "ge" => date >= today.AddDays(bound.Days[0]),
                "in" => bound.Days.Any(d => date == today.AddDays(d)),
                _ => false,
            };
            if (!holds) return false;
        }

        return true;
    }

    // ── Clauses, facts and witnesses (§0.3, D-23) ───────────────────────────────────────────────────────────────────

    private sealed record Clause(int Index, string Related, string Path, bool IsExists, JsonElement Filter);

    private static List<Clause> ReadClauses(JsonElement root)
    {
        var clauses = new List<Clause>();
        if (!root.TryGetProperty("all", out var all) || all.ValueKind != JsonValueKind.Array)
        {
            return clauses;
        }

        var index = 0;
        foreach (var clause in all.EnumerateArray())
        {
            var isExists = clause.TryGetProperty("exists", out var existsEl);
            var related = isExists ? existsEl.GetString()! : clause.GetProperty("notExists").GetString()!;
            clauses.Add(new Clause(index, related, clause.GetProperty("path").GetString()!, isExists, clause.GetProperty("filter").Clone()));
            index++;
        }

        return clauses;
    }

    /// <summary>The literal each POSITIVE clause pins a field to: a scalar, <c>{"=": v}</c> or a one-element list. A
    /// relative-date pin is the instant the predicate tested. Never a <c>notExists</c> field.</summary>
    private static List<(string Field, object? Value)> ReadPinnedFacts(IEnumerable<Clause> clauses, DateTimeOffset anchor)
    {
        var facts = new List<(string, object?)>();
        foreach (var clause in clauses.Where(c => c.IsExists))
        {
            foreach (var field in clause.Filter.EnumerateObject())
            {
                JsonElement? pinned = field.Value.ValueKind switch
                {
                    JsonValueKind.Array when field.Value.GetArrayLength() == 1 => field.Value[0],
                    JsonValueKind.Object when field.Value.EnumerateObject().Count() == 1
                        && field.Value.EnumerateObject().First().Name == "=" => field.Value.EnumerateObject().First().Value,
                    JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => field.Value,
                    _ => null,
                };
                if (pinned is { } p)
                {
                    facts.Add((field.Name, JsonLiteral(p, anchor)));
                }
            }
        }

        return facts;
    }

    private static object? JsonLiteral(JsonElement value, DateTimeOffset anchor) => value.ValueKind switch
    {
        JsonValueKind.String when PredicateCompiler.TryParseRelativeDays(value.GetString(), out var days) => anchor.AddDays(days),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>The first field of a clause filter compared with a relative date: the witness's "as of" date.</summary>
    private static string? DateFieldOf(Clause clause)
    {
        foreach (var field in clause.Filter.EnumerateObject())
        {
            var value = field.Value;
            if (value.ValueKind == JsonValueKind.String && PredicateCompiler.TryParseRelativeDays(value.GetString(), out _))
            {
                return field.Name;
            }

            if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any(op =>
                    op.Value.ValueKind == JsonValueKind.String && PredicateCompiler.TryParseRelativeDays(op.Value.GetString(), out _)))
            {
                return field.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// One witness per <c>exists</c> clause per hit: the clause's own compiled filter (copied from the compiled link-entity,
    /// so the witness satisfies exactly what was tested) plus the join to the hit, latest date first.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<SignalEvidenceRef>>> ReadWitnessesAsync(
        XElement compiledEntity, IReadOnlyList<Clause> clauses, IReadOnlyList<Guid> subjectIds, DateTimeOffset anchor, CancellationToken ct)
    {
        var lines = new Dictionary<Guid, List<SignalEvidenceRef>>();
        if (subjectIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<SignalEvidenceRef>>();
        }

        foreach (var clause in clauses.Where(c => c.IsExists))
        {
            var alias = "c" + clause.Index.ToString(CultureInfo.InvariantCulture);
            var link = compiledEntity.Elements("link-entity").FirstOrDefault(l => (string?)l.Attribute("alias") == alias)
                ?? throw new InvalidOperationException($"The compiled predicate has no link-entity '{alias}' for clause all[{clause.Index}].");
            var compiledFilter = link.Element("filter");
            WitnessColumnsByEntity.TryGetValue(clause.Related, out var display);
            var dateField = DateFieldOf(clause);

            foreach (var batch in subjectIds.Chunk(PredicateCompiler.MaxInListLength))
            {
                var witnessEntity = new XElement("entity", new XAttribute("name", clause.Related),
                    new XElement("attribute", new XAttribute("name", clause.Related + "id")),
                    new XElement("attribute", new XAttribute("name", clause.Path)));
                foreach (var column in new[] { display.Text, display.Source, dateField }.OfType<string>().Distinct(StringComparer.Ordinal))
                {
                    witnessEntity.Add(new XElement("attribute", new XAttribute("name", column)));
                }

                if (dateField is not null)
                {
                    witnessEntity.Add(new XElement("order", new XAttribute("attribute", dateField), new XAttribute("descending", "true")));
                }

                witnessEntity.Add(new XElement("order", new XAttribute("attribute", clause.Related + "id")));
                var filter = new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", clause.Path), new XAttribute("operator", "in"),
                        batch.Select(id => new XElement("value", id.ToString("D", CultureInfo.InvariantCulture)))));
                if (compiledFilter is not null)
                {
                    filter.Add(new XElement(compiledFilter));
                }

                witnessEntity.Add(filter);
                var rows = await RetrieveAllAsync(new XDocument(new XElement("fetch", witnessEntity)), ct).ConfigureAwait(false);

                foreach (var row in rows)
                {
                    var subjectId = row.GetAttributeValue<EntityReference>(clause.Path)?.Id ?? Guid.Empty;
                    if (subjectId == Guid.Empty)
                    {
                        continue;
                    }

                    if (!lines.TryGetValue(subjectId, out var list))
                    {
                        list = new List<SignalEvidenceRef>();
                        lines[subjectId] = list;
                    }

                    var clauseId = $"all[{clause.Index.ToString(CultureInfo.InvariantCulture)}]";
                    if (list.Any(l => l.Clause == clauseId))
                    {
                        continue; // ordered latest first: the first row is the witness
                    }

                    var asOf = dateField is null ? null : row.GetAttributeValue<DateTime?>(dateField);
                    list.Add(new SignalEvidenceRef(
                        Kind: clause.Related.StartsWith("sprk_", StringComparison.Ordinal) ? clause.Related[5..] : clause.Related,
                        Ref: $"{clause.Related}:{row.Id.ToString("D", CultureInfo.InvariantCulture)}",
                        Tier: "Fact")
                    {
                        Text = display.Text is null ? null : TextOf(row, display.Text),
                        Source = display.Source is null ? null : TextOf(row, display.Source),
                        AsOf = asOf is { } t
                            ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                            : null,
                        Clause = clauseId,
                    });
                }
            }
        }

        return lines.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<SignalEvidenceRef>)kv.Value);
    }

    // ── Reads ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record CatalogFields(string? DisplayField, string? NumberField);

    /// <summary>D-36: the subject table's display-name and number fields come from its <c>sprk_recordtype_ref</c> catalog row.
    /// A table with no catalog row (communication, verified live 2026-10-10) has neither.</summary>
    private async Task<CatalogFields> ReadCatalogFieldsAsync(string subjectEntity, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_recordtype_ref")
        {
            ColumnSet = new ColumnSet("sprk_recorddisplaynamefield", "sprk_regardingrecordnumberfield"),
            TopCount = 2,
        };
        query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, subjectEntity);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (rows.Entities.Count != 1)
        {
            return new CatalogFields(null, null);
        }

        static string? Field(Entity row, string column) =>
            row.GetAttributeValue<string>(column)?.Trim() is { Length: > 0 } f && IsLogicalName(f) ? f : null;
        return new CatalogFields(Field(rows.Entities[0], "sprk_recorddisplaynamefield"), Field(rows.Entities[0], "sprk_regardingrecordnumberfield"));
    }

    private static bool IsLogicalName(string value) =>
        value.Length > 0 && char.IsAsciiLetterLower(value[0]) && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    /// <summary>Pages a FetchXML query to the end (paging cookie). More than <see cref="MaxPages"/> pages throws.</summary>
    private async Task<List<Entity>> RetrieveAllAsync(XDocument fetch, CancellationToken ct)
    {
        var rows = new List<Entity>();
        string? cookie = null;
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
            {
                throw new InvalidOperationException(
                    $"The evaluation read more than {MaxPages} pages of {PageSize} rows. Stopping rather than narrowing the pass " +
                    "(task 031 escalation: a full re-evaluation is too expensive at this volume).");
            }

            var root = fetch.Root!;
            root.SetAttributeValue("count", PageSize.ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("page", page.ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("paging-cookie", cookie);
            var result = await _entities.RetrieveMultipleAsync(new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)), ct)
                .ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords)
            {
                return rows;
            }

            cookie = result.PagingCookie;
        }
    }

    private static XElement Condition(string attribute, string op, string value) =>
        new("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op), new XAttribute("value", value));

    private static string? TextOf(Entity row, string column) =>
        row.Attributes.TryGetValue(column, out var value) ? NormalizeValue(value)?.ToString() : null;

    /// <summary>An SDK attribute value as the plain fact the predicate compared (option value, lookup id, money amount).</summary>
    internal static object? NormalizeValue(object? value) => value switch
    {
        null => null,
        AliasedValue aliased => NormalizeValue(aliased.Value),
        OptionSetValue option => option.Value,
        OptionSetValueCollection options => options.Select(o => o.Value).ToArray(),
        EntityReference reference => reference.Id,
        Money money => money.Value,
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
        _ => value,
    };
}
