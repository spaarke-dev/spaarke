using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Pragmatic structural validator for the OpenAI function-parameters JSON-Schema
/// subset — the H1 fix of the G-P3 UAT round-1 incident (2026-07-07): the
/// CREATE-TASK@v1 Action row carried <c>"required": true</c> INSIDE a property
/// definition, and because Azure OpenAI validates every known JSON-Schema keyword
/// anywhere in every projected tool schema, that ONE malformed catalog row
/// 400-failed EVERY text-path turn (<c>invalid_function_parameters</c> rejects
/// the whole request, not the one tool). This validator runs at projection time
/// so an invalid schema excludes ONLY its own tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — the only
/// schema validation on the projection path is
/// <c>ToolHandlerToAIFunctionAdapter.ValidateAgainstMetaSchema</c> (Draft 2020-12
/// meta-schema via JsonSchema.Net), which is adapter-internal, throw-based, and
/// checks JSON-Schema validity — NOT the stricter OpenAI subset (e.g. a
/// type=array schema without <c>items</c> is VALID Draft 2020-12 but rejected by
/// OpenAI). (2) <i>Extension</i> — the Binding projection
/// (<see cref="BindingCapabilityTool"/> / <see cref="RefusalCapabilityTool"/>)
/// needs non-throwing exclude-one-tool semantics and the health check needs a
/// reusable per-row scan; neither can reuse a constructor-throwing private
/// method. (3) <i>Cost-of-doing-nothing</i> — the UAT incident verbatim: every
/// loop turn on the tenant fails with a raw HTTP 400 banner until an operator
/// hand-edits the catalog row.
/// </para>
/// <para>
/// <b>What is deliberately tolerated</b> (mirrors observed Azure OpenAI
/// behavior, non-strict mode):
/// </para>
/// <list type="bullet">
///   <item>Unknown keywords (<c>elicitation_prompt</c>, <c>ledger_resolution</c>,
///   the legacy <c>args</c> wrapper) — OpenAI ignores them; they carry maker
///   metadata.</item>
///   <item>A missing root <c>type</c> — OpenAI accepted the legacy
///   <c>{"args":[...]}</c> rows for months; requiring <c>type: object</c> at the
///   root would retro-break un-normalized environments for no protocol gain.</item>
///   <item>Null / whitespace / malformed-JSON / non-object-root schema text —
///   the projections already degrade those to a safe default schema BEFORE this
///   validator matters (routing tolerance rule); <see cref="FindFirstError"/>
///   returns null for them.</item>
/// </list>
/// <para>
/// <b>What is rejected</b> (each of these poisons the ENTIRE request when sent
/// inside any one tool's <c>function.parameters</c>):
/// </para>
/// <list type="bullet">
///   <item><c>required</c> that is not an array of strings, anywhere (the exact
///   UAT payload: property-level <c>"required": true</c>).</item>
///   <item><c>type</c> that is not a legal type name (or array of legal names).</item>
///   <item>A root <c>type</c> present but not <c>object</c>.</item>
///   <item><c>properties</c> that is not an object, or a property value that is
///   not a schema object.</item>
///   <item>A <c>type: array</c> schema without <c>items</c>, or <c>items</c>
///   that is not a schema object / array of schema objects.</item>
///   <item><c>enum</c> that is not an array; <c>description</c> that is not a
///   string; <c>additionalProperties</c> that is neither boolean nor object;
///   <c>anyOf</c>/<c>oneOf</c>/<c>allOf</c> that is not an array of schema
///   objects.</item>
/// </list>
/// <para>
/// <b>NFR-07</b>: returned error strings contain only JSON-Schema keyword paths
/// and JSON value kinds — deterministic catalog structure, never user content.
/// </para>
/// </remarks>
public static class OpenAiFunctionSchemaValidator
{
    private static readonly HashSet<string> LegalTypeNames = new(StringComparer.Ordinal)
    {
        "object", "string", "number", "integer", "boolean", "array", "null",
    };

    private static readonly string[] CompositionKeywords = { "anyOf", "oneOf", "allOf" };

    /// <summary>
    /// Validates raw catalog schema text (e.g. <c>sprk_inputschema</c> /
    /// <c>sprk_jsonschema</c>) against the OpenAI function-parameters subset.
    /// Returns <c>null</c> when the text is safe to project (including the
    /// degrade-to-default cases the projections handle upstream: null/whitespace,
    /// malformed JSON, non-object root); otherwise the FIRST validation error
    /// (keyword path + reason — NFR-07 safe).
    /// </summary>
    public static string? FindFirstError(string? rawSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(rawSchemaJson))
        {
            return null; // Projections substitute their default schema — nothing reaches OpenAI.
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawSchemaJson);
        }
        catch (JsonException)
        {
            return null; // Malformed JSON degrades to the default schema upstream (routing tolerance).
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null; // Non-object root degrades to the default schema upstream.
            }

            // Root-specific rule: when a root type IS declared it must be object —
            // function parameters are always an object envelope.
            if (doc.RootElement.TryGetProperty("type", out var rootType) &&
                rootType.ValueKind == JsonValueKind.String &&
                !string.Equals(rootType.GetString(), "object", StringComparison.Ordinal))
            {
                return $"root 'type' must be 'object' for function parameters (was '{rootType.GetString()}')";
            }

            return ValidateSchemaObject(doc.RootElement, "$");
        }
    }

    /// <summary>
    /// Recursive structural walk of one schema object. Returns the first error
    /// or <c>null</c>. <paramref name="path"/> is the keyword path for the error
    /// message (e.g. <c>$.properties.due_date</c>).
    /// </summary>
    private static string? ValidateSchemaObject(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return $"{path}: schema must be a JSON object (was {schema.ValueKind})";
        }

        // ── type ────────────────────────────────────────────────────────────
        string? declaredType = null;
        if (schema.TryGetProperty("type", out var typeEl))
        {
            switch (typeEl.ValueKind)
            {
                case JsonValueKind.String:
                    declaredType = typeEl.GetString();
                    if (declaredType is null || !LegalTypeNames.Contains(declaredType))
                    {
                        return $"{path}.type: '{declaredType}' is not a legal JSON-Schema type";
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var t in typeEl.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.String ||
                            t.GetString() is not { } name || !LegalTypeNames.Contains(name))
                        {
                            return $"{path}.type: type arrays must contain legal type-name strings";
                        }
                        if (string.Equals(t.GetString(), "array", StringComparison.Ordinal))
                        {
                            declaredType = "array";
                        }
                    }
                    break;
                default:
                    return $"{path}.type: must be a string or array of strings (was {typeEl.ValueKind})";
            }
        }

        // ── required — THE UAT killer ───────────────────────────────────────
        if (schema.TryGetProperty("required", out var requiredEl))
        {
            if (requiredEl.ValueKind != JsonValueKind.Array)
            {
                return $"{path}.required: must be an array of property names " +
                       $"(was {requiredEl.ValueKind} — property-level \"required\": true is not JSON Schema; " +
                       "list the property in the OBJECT-level required array instead)";
            }

            foreach (var item in requiredEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return $"{path}.required: entries must be strings (found {item.ValueKind})";
                }
            }
        }

        // ── description ─────────────────────────────────────────────────────
        if (schema.TryGetProperty("description", out var descEl) &&
            descEl.ValueKind != JsonValueKind.String)
        {
            return $"{path}.description: must be a string (was {descEl.ValueKind})";
        }

        // ── enum ────────────────────────────────────────────────────────────
        if (schema.TryGetProperty("enum", out var enumEl) &&
            enumEl.ValueKind != JsonValueKind.Array)
        {
            return $"{path}.enum: must be an array (was {enumEl.ValueKind})";
        }

        // ── properties ──────────────────────────────────────────────────────
        if (schema.TryGetProperty("properties", out var propsEl))
        {
            if (propsEl.ValueKind != JsonValueKind.Object)
            {
                return $"{path}.properties: must be an object (was {propsEl.ValueKind})";
            }

            foreach (var prop in propsEl.EnumerateObject())
            {
                var error = ValidateSchemaObject(prop.Value, $"{path}.properties.{prop.Name}");
                if (error is not null)
                {
                    return error;
                }
            }
        }

        // ── items (+ the OpenAI array-requires-items rule) ──────────────────
        var hasItems = schema.TryGetProperty("items", out var itemsEl);
        if (string.Equals(declaredType, "array", StringComparison.Ordinal) && !hasItems)
        {
            return $"{path}: array schemas must declare 'items' (OpenAI rejects array schemas without items)";
        }

        if (hasItems)
        {
            switch (itemsEl.ValueKind)
            {
                case JsonValueKind.Object:
                    var itemError = ValidateSchemaObject(itemsEl, $"{path}.items");
                    if (itemError is not null)
                    {
                        return itemError;
                    }
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in itemsEl.EnumerateArray())
                    {
                        var tupleError = ValidateSchemaObject(item, $"{path}.items[{i}]");
                        if (tupleError is not null)
                        {
                            return tupleError;
                        }
                        i++;
                    }
                    break;
                default:
                    return $"{path}.items: must be a schema object or array of schema objects (was {itemsEl.ValueKind})";
            }
        }

        // ── additionalProperties ────────────────────────────────────────────
        if (schema.TryGetProperty("additionalProperties", out var apEl))
        {
            switch (apEl.ValueKind)
            {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    break;
                case JsonValueKind.Object:
                    var apError = ValidateSchemaObject(apEl, $"{path}.additionalProperties");
                    if (apError is not null)
                    {
                        return apError;
                    }
                    break;
                default:
                    return $"{path}.additionalProperties: must be a boolean or schema object (was {apEl.ValueKind})";
            }
        }

        // ── anyOf / oneOf / allOf ───────────────────────────────────────────
        foreach (var keyword in CompositionKeywords)
        {
            if (!schema.TryGetProperty(keyword, out var compEl))
            {
                continue;
            }

            if (compEl.ValueKind != JsonValueKind.Array)
            {
                return $"{path}.{keyword}: must be an array of schema objects (was {compEl.ValueKind})";
            }

            var j = 0;
            foreach (var branch in compEl.EnumerateArray())
            {
                var branchError = ValidateSchemaObject(branch, $"{path}.{keyword}[{j}]");
                if (branchError is not null)
                {
                    return branchError;
                }
                j++;
            }
        }

        return null;
    }
}
