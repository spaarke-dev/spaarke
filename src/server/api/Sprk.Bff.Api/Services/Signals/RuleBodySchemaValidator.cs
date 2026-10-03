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
/// <b>Not yet wired to the Dataverse save path.</b> Task 022 ("Rule-body validation refusal") is the task
/// that calls this validator on create/update of <c>sprk_policyversion</c>. This task only builds the
/// validation seam itself and proves it refuses the CM-3 cross-clause-variable violation at the schema
/// level (see the escalation trigger in task 020's POML).
/// </para>
/// </remarks>
public sealed class RuleBodySchemaValidator
{
    private const string ExistenceSchemaResourceSuffix = "Services.Signals.Schemas.existence-rule.schema.json";

    private static readonly Lazy<JsonSchema> ExistenceSchema = new(LoadExistenceSchema);

    /// <summary>
    /// Validates <paramref name="ruleBodyJson"/> against the schema for <paramref name="ruleType"/>.
    /// </summary>
    public RuleBodyValidationResult Validate(RuleType ruleType, string? ruleBodyJson)
    {
        if (string.IsNullOrWhiteSpace(ruleBodyJson))
        {
            return RuleBodyValidationResult.Failure("sprk_rulebody is required and cannot be blank.");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(ruleBodyJson);
        }
        catch (JsonException ex)
        {
            return RuleBodyValidationResult.Failure($"sprk_rulebody is not valid JSON: {ex.Message}");
        }

        if (node is null)
        {
            return RuleBodyValidationResult.Failure("sprk_rulebody parsed to a null JSON value.");
        }

        var schema = ResolveSchema(ruleType);

        var results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

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
    /// True only for the three closed <see cref="RuleType"/> members, by name (case-insensitive) or by their
    /// Dataverse option-set numeric value as a string.
    /// </summary>
    public static bool TryParseRuleType(string? ruleTypeRaw, out RuleType ruleType)
    {
        ruleType = default;

        if (string.IsNullOrWhiteSpace(ruleTypeRaw))
        {
            return false;
        }

        if (Enum.TryParse(ruleTypeRaw, ignoreCase: true, out RuleType parsed) && Enum.IsDefined(parsed))
        {
            ruleType = parsed;
            return true;
        }

        if (int.TryParse(ruleTypeRaw, out var numeric) && Enum.IsDefined(typeof(RuleType), numeric))
        {
            ruleType = (RuleType)numeric;
            return true;
        }

        return false;
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
