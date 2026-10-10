using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The outcome of validating a <c>sprk_policyversion.sprk_rulebody</c> value against the JSON Schema for its
/// declared <see cref="RuleType"/>. Carries field-level error strings so a save-time caller (task 022) can
/// surface them, rather than a bare boolean.
/// </summary>
public sealed record RuleBodyValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static RuleBodyValidationResult Success() => new(true, Array.Empty<string>());

    public static RuleBodyValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);

    public static RuleBodyValidationResult Failure(string error) => new(false, new[] { error });
}

/// <summary>
/// Validates a <c>sprk_policyversion.sprk_rulebody</c> JSON value against the JSON Schema for its
/// <c>sprk_ruletype</c> (spec FR-05, FR-08; design.md CM-7 / section 8.0.1(a)).
/// </summary>
/// <remarks>
/// <para>
/// <b>Closed set, not a registry.</b> Per project CLAUDE.md §3.4 and design.md CM-7/CM-8, the mapping from
/// <see cref="RuleType"/> to schema is a plain, exhaustive C# <c>switch</c> — mirroring how
/// <c>MatterLiveFactResolver</c>'s predicate dispatch is intentionally closed rather than generic. Adding a
/// rule type is a code change with review, never a configuration row.
/// </para>
/// <para>
/// <b>Scope of this task (020).</b> Only <see cref="RuleType.Existence"/> has a shipped schema — it is the
/// one differentiated capability that is otherwise literally unsavable (spec FR-05 rationale). No
/// <see cref="RuleType.Threshold"/> or <see cref="RuleType.Switch"/> <c>sprk_policyversion</c> rows exist yet
/// in this project; authoring their schemas is explicitly out of this task's scope and is deferred to
/// whichever future task first seeds a row of that type.
/// </para>
/// <para>
/// <b>ADR-013 compliance.</b> This validator consumes only the raw <c>sprk_rulebody</c> JSON string and
/// JSON Schema metadata — no AI-internal types, no <c>Services/Ai/*</c> dependency, no Dataverse reads.
/// </para>
/// <para>
/// <b>Wired via <see cref="PolicyVersionValidator"/> (task 022).</b> That class calls this validator first
/// (shape), then <see cref="PredicateCompiler"/> (strictly more than shape) on create/update of
/// <c>sprk_policyversion</c> and again at evaluation time (the owner's fail-closed decision — a policy row
/// can be authored directly in the Spaarke Platform app, bypassing the BFF). This class proves it refuses the
/// CM-3 cross-clause-variable violation at the schema level (see the escalation trigger in task 020's POML).
/// See <see cref="Validate"/> for its exact throwing contract (one case: a rule type with no authored schema).
/// </para>
/// </remarks>
public sealed class RuleBodySchemaValidator
{
    private const string ExistenceSchemaResourceSuffix = "Services.Signals.Schemas.existence-rule.schema.json";

    private static readonly Lazy<JsonSchema> ExistenceSchema = new(LoadExistenceSchema);

    /// <summary>Serializes every call into <c>JsonSchema.Evaluate</c> against the shared
    /// <see cref="ExistenceSchema"/> instance — see <see cref="Validate"/>'s own remarks for the measured
    /// thread-safety bug this guards against. Static (not per-instance) because the race is on the SHARED
    /// schema object, not on <see cref="RuleBodySchemaValidator"/> itself, and this type is a DI singleton
    /// anyway (so a per-instance lock would have been equivalent in practice, but static states the real
    /// invariant: one schema, one evaluation at a time, regardless of how many validator instances exist).</summary>
    private static readonly object EvaluationGate = new();

    /// <summary>Duplicate object keys are refused at ANY nesting depth (top-level AND inside a nested
    /// <c>filter</c>) rather than silently accepted — task 022 review finding #1: with the default
    /// <c>AllowDuplicateProperties = true</c>, a duplicate key reaching <see cref="JsonNode"/> construction
    /// throws an unhandled <see cref="ArgumentException"/> from <c>JsonObject</c>'s internal dictionary
    /// insert, escaping this validator entirely. Parsing strictly here converts that into an ordinary,
    /// catchable <see cref="JsonException"/> before a <see cref="JsonNode"/> is ever built.</summary>
    private static readonly JsonDocumentOptions StrictJson = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// The longest <c>sprk_rulebody</c> (in UTF-16 chars) any validator or the compiler will parse. The shipped
    /// Path B body is ~0.6 KB (<c>tests/fixtures/signals/pathb-existence.rulebody.json</c>); 32 KB is over 50x
    /// that. Defined HERE — the lowest layer — so <see cref="Validate"/>, <see cref="PolicyVersionValidator"/>
    /// and <see cref="PredicateCompiler"/> share one number (<see cref="PredicateCompiler.MaxRuleBodyLength"/>
    /// aliases it). Task 022 rework round 2, finding F2: checked BEFORE any parse or schema evaluation, because
    /// schema evaluation runs under the process-wide <see cref="EvaluationGate"/> lock and a 0.9 MB body was
    /// measured holding that lock ~3.7 s per evaluation — one oversized row would stall every other caller.
    /// </summary>
    public const int MaxRuleBodyLength = 32_768;

    /// <summary>The ONE property name that may never appear anywhere inside a rule body (task 022 rework round
    /// 2, finding F8). The sentence template's single source is the <c>sprk_policyversion.sprk_messagetemplate</c>
    /// column (schema-draft.md §4, spec.md NFR-02); the early-draft <c>then.messageTemplate</c> location
    /// (mvp-technical-spec.md §3.3, ontology-architecture-feedback.md §3.1) was superseded by that column and is
    /// never read by the evaluator or checked against the predicate's read set — so a body carrying one would
    /// hold unvalidated §0.3 prose. Matched case-insensitively at any depth.</summary>
    public const string ForbiddenMessageTemplateProperty = "messageTemplate";

    /// <summary>The top-level rule-body property refused as reserved (task 022 rework round 3, finding L2,
    /// coordinator decision): early drafts put the Signal's type/severity/template/action under <c>then</c>, but
    /// those now live in their own <c>sprk_policyversion</c> columns and no task defines or reads <c>then</c>.</summary>
    public const string ReservedThenProperty = "then";

    /// <summary>
    /// The bounded refusal message for an over-length body (finding F2). Shared so every layer reports the same
    /// text and a caller can recognise it.
    /// </summary>
    public static string RuleBodyTooLongMessage(int length) =>
        $"sprk_rulebody is {length} characters, exceeding the {MaxRuleBodyLength}-character limit.";

    /// <summary>
    /// Validates <paramref name="ruleBodyJson"/> against the schema for <paramref name="ruleType"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Exactly one thing throws</b> (task 022 rework round 2, finding F9 — the previous "never throws
    /// for ANY input" claim was false): a <paramref name="ruleType"/> with no authored schema
    /// (<see cref="RuleType.Threshold"/> / <see cref="RuleType.Switch"/>, task 020 scope) throws
    /// <see cref="NotSupportedException"/>, for ANY body, before the body is read — that is a caller-contract
    /// question about the TYPE, not a property of the authored content. <see cref="PolicyVersionValidator"/>
    /// never reaches it (it refuses those types first as <c>rule_type_unsupported</c>).</para>
    /// <para>For <see cref="RuleType.Existence"/> it never throws for any body, however pathological: blank,
    /// over-length (refused before parsing, finding F2), malformed or duplicate-key JSON, an unpaired UTF-16
    /// surrogate escape in a property name or a value (round 3, finding M1), a forbidden <c>messageTemplate</c>
    /// property (finding F8), the reserved <c>then</c> property (round 3, finding L2), or a number such as
    /// <c>1e999999</c> whose schema evaluation throws <see cref="FormatException"/>/<see cref="OverflowException"/>
    /// inside the library — all become an ordinary <see cref="RuleBodyValidationResult.Failure(string)"/>.</para>
    /// </remarks>
    /// <exception cref="NotSupportedException"><paramref name="ruleType"/> has no authored schema.</exception>
    public RuleBodyValidationResult Validate(RuleType ruleType, string? ruleBodyJson)
    {
        // Resolved FIRST so the one throwing case does not depend on the body's content (finding F9).
        var schema = ResolveSchema(ruleType);

        if (string.IsNullOrWhiteSpace(ruleBodyJson))
        {
            return RuleBodyValidationResult.Failure("sprk_rulebody is required and cannot be blank.");
        }

        // Bounded refusal BEFORE any parse or schema evaluation (finding F2) -- see MaxRuleBodyLength.
        if (ruleBodyJson.Length > MaxRuleBodyLength)
        {
            return RuleBodyValidationResult.Failure(RuleBodyTooLongMessage(ruleBodyJson.Length));
        }

        JsonNode? node;
        try
        {
            // Strict parse FIRST (AllowDuplicateProperties = false applies recursively to every nesting depth,
            // so this catches a duplicate key inside a clause's "filter" exactly as it catches a top-level
            // duplicate — ONE option, not a depth-by-depth special case). Only once that succeeds do we build
            // the JsonNode tree the schema evaluator needs, from the already-validated JsonElement -- never
            // from the raw text again, so a duplicate can never reach JsonNode construction's unguarded
            // dictionary insert.
            using var strictDoc = JsonDocument.Parse(ruleBodyJson, StrictJson);

            // Finding F8: refuse a body-embedded message template anywhere in the tree (cheap, bounded by the
            // length cap above) before paying for schema evaluation.
            var forbiddenAt = FindForbiddenMessageTemplate(strictDoc.RootElement, string.Empty);
            if (forbiddenAt is not null)
            {
                return RuleBodyValidationResult.Failure(
                    $"{forbiddenAt}: a message template is not permitted inside sprk_rulebody. The sentence " +
                    "template's single source is the sprk_messagetemplate column, which is the only template " +
                    "checked against what the predicate reads (section 0.3).");
            }

            // Task 022 rework round 3, finding L2: 'then' is RESERVED. No task defines it and nothing reads it, so
            // an open object there could only carry unvalidated content (e.g. a messageTemplate lookalike).
            if (strictDoc.RootElement.ValueKind == JsonValueKind.Object
                && strictDoc.RootElement.TryGetProperty(ReservedThenProperty, out _))
            {
                return RuleBodyValidationResult.Failure(
                    $"/{ReservedThenProperty}: '{ReservedThenProperty}' is reserved; no task defines it yet, and no " +
                    "code reads it. Remove it from sprk_rulebody (the Signal's sentence, headline and proposed " +
                    "action live in their own sprk_policyversion columns).");
            }

            node = JsonSerializer.SerializeToNode(strictDoc.RootElement);
        }
        catch (JsonException ex)
        {
            return RuleBodyValidationResult.Failure($"sprk_rulebody is not valid JSON: {ex.Message}");
        }
        catch (InvalidOperationException)
        {
            // Task 022 rework round 3, finding M1: an unpaired UTF-16 surrogate escape (e.g. "\ud800") in a
            // PROPERTY NAME throws InvalidOperationException ("Cannot read incomplete UTF-16 JSON text") wherever
            // the name is unescaped -- inside Parse's own duplicate-name check (AllowDuplicateProperties=false), or
            // in the name walk / TryGetProperty below it. In a VALUE the same escape surfaces as a JsonException
            // from SerializeToNode, handled above. Either way the text is not valid JSON for this validator's
            // purposes -- a refusal, never an escaping exception. Fixed message: the exception text is not echoed.
            return RuleBodyValidationResult.Failure(
                "sprk_rulebody is not valid JSON: it contains text that cannot be read as a string (for example " +
                "an unpaired UTF-16 surrogate escape such as \\ud800 in a property name).");
        }

        if (node is null)
        {
            return RuleBodyValidationResult.Failure("sprk_rulebody parsed to a null JSON value.");
        }

        EvaluationResults results;
        try
        {
            // SERIALIZED (review finding #1, discovered during the rework's own test run, not by the
            // reviewer's probe): Json.Schema.Net 7.3.4's JsonSchema.Evaluate is NOT thread-safe for
            // CONCURRENT calls against the SAME JsonSchema instance -- and ExistenceSchema is a process-wide
            // static singleton, evaluated by every PolicyVersionValidator call (itself an AddSingleton), so
            // concurrent calls WILL happen under real load. Measured directly against this exact package
            // version (throwaway repro, 2000-4000 iterations, Parallel.For): un-synchronized concurrent
            // Evaluate() calls on one schema instance produced wrong IsValid=true results for an ACTUALLY
            // INVALID body at a ~40% rate, and in a second run threw an unhandled IndexOutOfRangeException
            // from inside Json.Schema.SchemaConstraint.BuildEvaluation. A lock around the Evaluate call
            // eliminated BOTH symptoms across the same iteration counts. This is a correctness bug with the
            // owner's fail-closed mandate at stake -- a false IsValid=true is a FAIL-OPEN, not merely a flaky
            // test -- so it is fixed here rather than left as "probably a test-parallelism quirk".
            lock (EvaluationGate)
            {
                results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            // An extreme-but-syntactically-valid JSON number (e.g. 1e999999) throws FormatException out of
            // the schema library's own JsonElement.GetDecimal call during numeric-keyword evaluation;
            // ArgumentException/IndexOutOfRangeException are caught too, defensively, for the same class of
            // internal-library fragility the lock above is the primary defense against.
            return RuleBodyValidationResult.Failure(
                $"sprk_rulebody could not be evaluated against its schema ({ex.GetType().Name}): a value is " +
                "likely out of the numeric range this validator supports, or the schema library hit an " +
                "internal error on this input.");
        }

        if (results.IsValid)
        {
            return RuleBodyValidationResult.Success();
        }

        var errors = results.Details
            .Where(d => d.HasErrors && d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(kv => $"{d.InstanceLocation}: {kv.Value}"))
            .Distinct()
            .ToArray();

        return RuleBodyValidationResult.Failure(
            errors.Length > 0 ? errors : new[] { "sprk_rulebody failed schema validation." });
    }

    /// <summary>
    /// Parses <paramref name="ruleTypeRaw"/> against the closed <see cref="RuleType"/> set and validates
    /// <paramref name="ruleBodyJson"/> if it is a recognized member. An unrecognized value (e.g. "Transition"
    /// or "Trend", both explicitly deferred per design.md CM-7/CM-8) is refused without attempting schema
    /// resolution.
    /// </summary>
    public RuleBodyValidationResult ValidateRaw(string? ruleTypeRaw, string? ruleBodyJson)
    {
        if (!TryParseRuleType(ruleTypeRaw, out var ruleType))
        {
            return RuleBodyValidationResult.Failure(
                $"Unknown sprk_ruletype '{ruleTypeRaw}'. The closed set is Threshold, Switch, Existence " +
                "(design.md CM-7); adding a type is a code change with review, not a data value.");
        }

        return Validate(ruleType, ruleBodyJson);
    }

    /// <summary>
    /// True only for the three closed <see cref="RuleType"/> members, by EXACT name (case-insensitive) or by
    /// their Dataverse option-set numeric value as a string. Deliberately does NOT use <see cref="Enum.TryParse"/>
    /// on the string directly — task 022 review finding #8: <c>Enum.TryParse</c> accepts a comma-separated list
    /// of names (e.g. <c>"Threshold,Switch"</c>) and OR's their underlying values together even though
    /// <see cref="RuleType"/> carries no <c>[Flags]</c> attribute, and the OR'd result can coincide with a
    /// defined member by accident of the chosen numeric values. Matching against each exact member name (no
    /// comma, no whitespace-splitting) closes that off entirely.
    /// </summary>
    public static bool TryParseRuleType(string? ruleTypeRaw, out RuleType ruleType)
    {
        ruleType = default;

        if (string.IsNullOrWhiteSpace(ruleTypeRaw))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<RuleType>())
        {
            if (string.Equals(ruleTypeRaw, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                ruleType = candidate;
                return true;
            }
        }

        // Task 022 rework round 2, finding F13: NumberStyles.None + InvariantCulture -- digits only. The default
        // int.TryParse(string) overload is NumberStyles.Integer in the CURRENT culture, which accepted
        // " +100000002 " (leading/trailing whitespace and a sign) as Existence: the same "lenient parse widens
        // the closed set" class of bug finding #8 closed for names.
        if (int.TryParse(ruleTypeRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric)
            && Enum.IsDefined(typeof(RuleType), numeric))
        {
            ruleType = (RuleType)numeric;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finding F8: the JSON Pointer of the first property named <see cref="ForbiddenMessageTemplateProperty"/>
    /// (case-insensitive) anywhere in <paramref name="element"/>, or <c>null</c> if none. Recursion depth is
    /// bounded by <see cref="JsonDocumentOptions.MaxDepth"/> (64 by default), already enforced by the parse.
    /// </summary>
    private static string? FindForbiddenMessageTemplate(JsonElement element, string pointer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPointer = pointer + "/" + property.Name.Replace("~", "~0", StringComparison.Ordinal)
                        .Replace("/", "~1", StringComparison.Ordinal);
                    if (string.Equals(property.Name, ForbiddenMessageTemplateProperty, StringComparison.OrdinalIgnoreCase))
                    {
                        return childPointer;
                    }

                    var found = FindForbiddenMessageTemplate(property.Value, childPointer);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindForbiddenMessageTemplate(item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture));
                    if (found is not null)
                    {
                        return found;
                    }

                    index++;
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Exhaustive, closed dispatch from <see cref="RuleType"/> to its shipped JSON Schema. See the type-level
    /// remarks for why Threshold/Switch are not yet schema-backed.
    /// </summary>
    private static JsonSchema ResolveSchema(RuleType ruleType) => ruleType switch
    {
        RuleType.Existence => ExistenceSchema.Value,
        RuleType.Threshold or RuleType.Switch => throw new NotSupportedException(
            $"Schema validation for rule type '{ruleType}' has not been authored yet (task 020 scope: " +
            "Existence only — spec FR-05). No sprk_policyversion row of this type exists in this project."),
        _ => throw new NotSupportedException(
            $"'{ruleType}' is not a member of the closed RuleType set (design.md CM-7/CM-8)."),
    };

    private static JsonSchema LoadExistenceSchema()
    {
        var assembly = typeof(RuleBodySchemaValidator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ExistenceSchemaResourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource ending in '{ExistenceSchemaResourceSuffix}' was not found in assembly " +
                $"'{assembly.FullName}'. Check the EmbeddedResource glob in Sprk.Bff.Api.csproj covers " +
                "Services/Signals/Schemas/*.json.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource stream '{resourceName}'.");
        using var reader = new StreamReader(stream);
        var schemaText = reader.ReadToEnd();

        return JsonSchema.FromText(schemaText);
    }
}
