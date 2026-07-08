using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// One declared input-schema argument relevant to elicitation (FR-P2-03).
/// <see cref="Prompt"/> is the maker-authored elicitation prompt
/// (<c>elicitation_prompt</c>, falling back to <c>description</c>) — the ONLY
/// wording source a clarifying turn may use for this field (spec MUST: grounded
/// outputs reference the declared schema, never invented fields).
/// </summary>
public sealed record ElicitationField(string Name, string? Prompt, string? Type);

/// <summary>
/// Pure validation of capability-invocation arguments against the Binding's
/// declared input schema (<c>sprk_analysisaction.sprk_inputschema</c>) —
/// FR-P2-03 loop-native elicitation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — nothing
/// parses <c>sprk_inputschema</c> for required-arg validation; the prompted
/// executor treats it as opaque JSON and <see cref="BindingCapabilityTool"/>
/// forwards it verbatim as the LLM function schema (grep: <c>sprk_inputschema</c>
/// consumers are schema pass-throughs only). (2) <i>Extension</i> — the parse is
/// pure logic shared by the tool's invocation seam AND the chat endpoint's
/// mid-elicitation framing; embedding it as private statics in either would force
/// the other to duplicate it. (3) <i>Cost-of-doing-nothing</i> — walkthrough
/// steps 10-12 fail concretely: a capability invoked with missing required args
/// executes with absent/fabricated values instead of producing a clarifying turn.
/// </para>
/// <para>
/// <b>Schema tolerance</b> (same rule as every catalog JSON consumer — maker data
/// degrades, never throws): both the standard JSON-schema <c>required: [names]</c>
/// array AND a per-property <c>"required": true</c> member are honored. Malformed
/// or absent schemas yield ZERO required fields (nothing to elicit — matches the
/// permissive default schema the tool projects for schema-less Bindings).
/// </para>
/// <para>
/// <b>Missing</b> means: the argument is absent, JSON <c>null</c>, or a
/// whitespace-only string. Anything else the model supplied counts as provided —
/// value QUALITY is the executor's concern; elicitation is about presence
/// (deterministic, no classification — ADR-039).
/// </para>
/// </remarks>
public static class BindingInputSchemaValidator
{
    /// <summary>
    /// Parses the declared REQUIRED fields (name + elicitation prompt + type) from
    /// the Binding's raw input-schema JSON. Malformed/absent schema → empty list.
    /// </summary>
    public static IReadOnlyList<ElicitationField> GetRequiredFields(string? inputSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            return Array.Empty<ElicitationField>();
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<ElicitationField>();
            }

            // Standard JSON-schema required array.
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("required", out var requiredEl) &&
                requiredEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        item.GetString() is { Length: > 0 } name)
                    {
                        requiredNames.Add(name);
                    }
                }
            }

            var fields = new List<ElicitationField>();
            if (root.TryGetProperty("properties", out var propsEl) &&
                propsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propsEl.EnumerateObject())
                {
                    var isRequired = requiredNames.Contains(prop.Name);

                    // Maker-tolerant twin: per-property "required": true.
                    if (!isRequired &&
                        prop.Value.ValueKind == JsonValueKind.Object &&
                        prop.Value.TryGetProperty("required", out var inlineReq) &&
                        inlineReq.ValueKind == JsonValueKind.True)
                    {
                        isRequired = true;
                    }

                    if (!isRequired)
                    {
                        continue;
                    }

                    string? prompt = null;
                    string? type = null;
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (prop.Value.TryGetProperty("elicitation_prompt", out var promptEl) &&
                            promptEl.ValueKind == JsonValueKind.String)
                        {
                            prompt = promptEl.GetString();
                        }
                        else if (prop.Value.TryGetProperty("description", out var descEl) &&
                                 descEl.ValueKind == JsonValueKind.String)
                        {
                            prompt = descEl.GetString();
                        }

                        if (prop.Value.TryGetProperty("type", out var typeEl) &&
                            typeEl.ValueKind == JsonValueKind.String)
                        {
                            type = typeEl.GetString();
                        }
                    }

                    fields.Add(new ElicitationField(prop.Name, prompt, type));
                    requiredNames.Remove(prop.Name);
                }
            }

            // Required names with no matching property declaration still elicit by name
            // (the maker declared them required; presence is checkable without a shape).
            foreach (var orphan in requiredNames)
            {
                fields.Add(new ElicitationField(orphan, Prompt: null, Type: null));
            }

            return fields;
        }
        catch (JsonException)
        {
            // Malformed maker schema — degrade to "nothing required" (routing/projection
            // tolerance rule; the tool already degrades the SAME JSON to a default schema).
            return Array.Empty<ElicitationField>();
        }
    }

    /// <summary>
    /// Returns the declared required fields NOT satisfied by <paramref name="arguments"/>.
    /// Empty result = the invocation may execute.
    /// </summary>
    public static IReadOnlyList<ElicitationField> FindMissingRequired(
        string? inputSchemaJson,
        AIFunctionArguments? arguments)
    {
        var required = GetRequiredFields(inputSchemaJson);
        if (required.Count == 0)
        {
            return Array.Empty<ElicitationField>();
        }

        var missing = new List<ElicitationField>();
        foreach (var field in required)
        {
            if (arguments is null ||
                !arguments.TryGetValue(field.Name, out var value) ||
                IsMissingValue(value))
            {
                missing.Add(field);
            }
        }

        return missing;
    }

    private static bool IsMissingValue(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(je.GetString()),
            _ => false,
        },
        _ => false,
    };
}
