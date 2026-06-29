using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using HandlebarsDotNet;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Template engine implementation using Handlebars.NET.
/// Provides secure, logic-less template rendering for playbook delivery nodes.
/// </summary>
/// <remarks>
/// <para>
/// This implementation:
/// </para>
/// <list type="bullet">
/// <item>Uses Handlebars.NET for template compilation and rendering</item>
/// <item>Supports nested object access (e.g., {{node.output.field}})</item>
/// <item>Handles missing variables gracefully (renders as empty string)</item>
/// <item>Caches compiled templates for performance</item>
/// </list>
/// </remarks>
public sealed class TemplateEngine : ITemplateEngine
{
    private readonly IHandlebars _handlebars;
    private readonly ILogger<TemplateEngine> _logger;

    // Regex to match {{variable}} patterns (including nested like {{a.b.c}})
    private static readonly Regex VariablePattern = new(
        @"\{\{([^{}]+)\}\}",
        RegexOptions.Compiled);

    public TemplateEngine(ILogger<TemplateEngine> logger)
    {
        _logger = logger;

        // Configure Handlebars with safe defaults
        _handlebars = Handlebars.Create(new HandlebarsConfiguration
        {
            // Throw on missing members would break graceful handling, so leave default (false)
            ThrowOnUnresolvedBindingExpression = false,
            // Don't HTML-encode output (we're not rendering HTML)
            NoEscape = true
        });

        // Register helper for safe property access that returns empty string for nulls
        _handlebars.RegisterHelper("safe", (context, arguments) =>
        {
            var value = arguments.Length > 0 ? arguments[0] : null;
            return value?.ToString() ?? string.Empty;
        });

        // Register `default` helper: {{default X 'Y'}} returns X if non-empty, else 'Y'.
        // Replaces broken `{{X ?? 'Y'}}` usage in playbook configs (FR-3H1.1, R3 task 001).
        // Handlebars.NET passes `UndefinedBindingResult` for unresolved variables (NOT null),
        // whose .ToString() returns the binding name — so we must treat it as "empty".
        _handlebars.RegisterHelper("default", (writer, ctx, args) =>
            writer.WriteSafeString(
                args.Length > 1 && IsNonEmptyValue(args[0])
                    ? args[0]!.ToString()
                    : args.ElementAtOrDefault(1)?.ToString() ?? ""));

        // Register `joinIds` helper: {{joinIds arr}} → comma-separated string suitable for
        // FetchXML `operator='in' value='...'` clauses. Used by playbooks consuming
        // LookupUserMembership node output (FR-1B.2 + FR-3H1.2, R3 task 002).
        // Behavior:
        //   - IEnumerable (List<string>, List<Guid>, arrays, JsonElement-derived List<object>) → "a,b,c"
        //   - Null / UndefinedBindingResult (unresolved binding) / non-enumerable scalar → ""
        //   - Empty enumerable → ""
        //   - Null elements within enumerable → empty token (preserves position, harmless for FetchXML IN)
        _handlebars.RegisterHelper("joinIds", (writer, ctx, args) =>
            writer.WriteSafeString(JoinIds(args.Length > 0 ? args[0] : null)));

        // R7 Wave 11 task 112 (Option B Layer 1 helpers): the standard set required by
        // the DAILY-BRIEFING-NARRATE playbook's runtime template expressions + future
        // narrative-output consumers (Insight Engine, Workspace UX). All helpers follow
        // the defensive contract established by `joinIds`: null / UndefinedBindingResult
        // / non-enumerable scalar → safe fallback (empty string OR empty enumerable);
        // strings treated as scalars (not as IEnumerable<char>). See:
        //   docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md (T111a)

        // {{json X}} — serialize value to JSON string (camelCase, no indent).
        // Used by playbook authors to inject a full object into the LLM's `## Input`
        // section as JSON text (e.g., `inputBinding.briefing = "{{json start}}"`).
        // Return-style helper (works in subexpressions: `(json X)`).
        _handlebars.RegisterHelper("json", (context, args) =>
            JsonSerializeForTemplate(args.Length > 0 ? args[0] : null));

        // {{map COLL 'fieldName'}} or {{map COLL 'nested.path'}} — extract field from each
        // item in COLL, returns flat enumerable. Supports dotted nested paths.
        // Return-style helper so it composes in subexpressions: `(distinct (map A 'b'))`.
        _handlebars.RegisterHelper("map", (context, args) =>
            MapField(
                args.Length > 0 ? args[0] : null,
                args.Length > 1 ? args[1]?.ToString() : null));

        // {{flatten COLL}} — flatten one level. Items that are enumerable get expanded;
        // scalar items pass through.
        _handlebars.RegisterHelper("flatten", (context, args) =>
            FlattenEnumerable(args.Length > 0 ? args[0] : null));

        // {{distinct COLL}} — case-insensitive distinct (ordinal), preserves first-occurrence order.
        _handlebars.RegisterHelper("distinct", (context, args) =>
            DistinctValues(args.Length > 0 ? args[0] : null));

        // {{concat A B C …}} — concatenate enumerables (scalars promoted to single-element);
        // nulls / UndefinedBindingResult skipped entirely.
        _handlebars.RegisterHelper("concat", (context, args) => ConcatArgs(args.ToArray()));

        // {{join SEP A B C …}} — first arg is separator (string); subsequent args
        // concatenated then joined. Distinct from Handlebars built-in `{{#each}}` join.
        _handlebars.RegisterHelper("join", (context, args) =>
        {
            if (args.Length < 1) return string.Empty;
            var sep = args[0]?.ToString() ?? string.Empty;
            var concatenated = ConcatArgs(args.Skip(1).ToArray());
            return string.Join(sep, concatenated.Select(v => v?.ToString() ?? string.Empty));
        });

        // R7 Wave 11 task 113: {{flatMap COLL 'nested.path'}} — eliminates the need for
        // inline lambda syntax in source playbooks. Equivalent to JavaScript's
        // `arr.flatMap(item => lookup(item, 'nested.path'))`.
        // Used by DAILY-BRIEFING-NARRATE ValidateEntityNames allowList expression (rewritten
        // from `(lambda c (map c.items 'regardingName'))` to `(flatMap COLL 'items.regardingName')`).
        // For each item in COLL, traverse the dotted path. If the path resolves to an
        // enumerable, expand it; if to a scalar, add as single element. Nulls skipped.
        _handlebars.RegisterHelper("flatMap", (context, args) =>
            FlatMapField(
                args.Length > 0 ? args[0] : null,
                args.Length > 1 ? args[1]?.ToString() : null));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R7 Wave 11 task 112 helper implementations
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonHelperOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    /// <summary>
    /// {{json X}} serialization. Null / UndefinedBindingResult → "null" literal (consistent
    /// with how the value would render in JSON). Strings pass through quoted.
    /// </summary>
    private static string JsonSerializeForTemplate(object? value)
    {
        if (value is null || value is UndefinedBindingResult)
            return "null";
        try
        {
            return JsonSerializer.Serialize(value, JsonHelperOptions);
        }
        catch (Exception)
        {
            return "null";
        }
    }

    /// <summary>
    /// {{map COLL 'field'}} core. COLL is enumerable; each item is walked via dotted
    /// path; result is flat list. Null COLL / non-enumerable / null fieldPath → empty list.
    /// </summary>
    private static IEnumerable<object?> MapField(object? collection, string? fieldPath)
    {
        if (collection is null || collection is UndefinedBindingResult || string.IsNullOrEmpty(fieldPath))
            return Array.Empty<object?>();

        if (collection is string)
            return Array.Empty<object?>();

        if (collection is not IEnumerable enumerable)
            return Array.Empty<object?>();

        var pathParts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<object?>();
        foreach (var item in enumerable)
        {
            result.Add(ResolveDottedPath(item, pathParts));
        }
        return result;
    }

    /// <summary>
    /// Walks dotted-path segments on an item that may be Dictionary, JsonElement, or
    /// a CLR object (via reflection — minimal use to support test scenarios).
    /// </summary>
    private static object? ResolveDottedPath(object? item, string[] pathParts)
    {
        var current = item;
        foreach (var part in pathParts)
        {
            if (current is null || current is UndefinedBindingResult)
                return null;

            switch (current)
            {
                case IDictionary<string, object?> dict:
                    current = dict.TryGetValue(part, out var v) ? v : null;
                    break;
                case IDictionary nonGenericDict when nonGenericDict.Contains(part):
                    current = nonGenericDict[part];
                    break;
                case JsonElement je when je.ValueKind == JsonValueKind.Object && je.TryGetProperty(part, out var prop):
                    current = ConvertJsonElement(prop);
                    break;
                default:
                    current = null;
                    break;
            }
        }
        return current;
    }

    /// <summary>
    /// {{flatten COLL}} core. One-level flatten — items that are themselves enumerable
    /// (and not strings) get expanded; scalar items pass through.
    /// </summary>
    private static IEnumerable<object?> FlattenEnumerable(object? value)
    {
        if (value is null || value is UndefinedBindingResult || value is string)
            return Array.Empty<object?>();

        if (value is not IEnumerable enumerable)
            return Array.Empty<object?>();

        var result = new List<object?>();
        foreach (var item in enumerable)
        {
            if (item is IEnumerable inner && item is not string)
            {
                foreach (var sub in inner)
                {
                    result.Add(sub);
                }
            }
            else
            {
                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>
    /// {{distinct COLL}} core. Case-insensitive ordinal distinct on .ToString(); preserves
    /// first-occurrence order. Null items skipped.
    /// </summary>
    private static IEnumerable<object?> DistinctValues(object? value)
    {
        if (value is null || value is UndefinedBindingResult || value is string)
            return Array.Empty<object?>();

        if (value is not IEnumerable enumerable)
            return Array.Empty<object?>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<object?>();
        foreach (var item in enumerable)
        {
            if (item is null) continue;
            var key = item.ToString();
            if (string.IsNullOrEmpty(key)) continue;
            if (seen.Add(key))
            {
                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>
    /// {{concat A B C …}} core. Each arg flattened to enumerable; nulls / UndefinedBindingResult
    /// skipped. Strings treated as scalars (promoted to single-element).
    /// </summary>
    private static IEnumerable<object?> ConcatArgs(IReadOnlyList<object?> args)
    {
        var result = new List<object?>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is null || arg is UndefinedBindingResult) continue;

            if (arg is string)
            {
                result.Add(arg);
                continue;
            }

            if (arg is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    result.Add(item);
                }
                continue;
            }

            // Non-enumerable scalar — promote to single element
            result.Add(arg);
        }
        return result;
    }

    /// <summary>
    /// Serializes an enumerable for safe interpolation into a string-rendered template
    /// (Handlebars writes the helper's string output verbatim — we need a JSON-ish
    /// representation so the output is round-trippable when the template result is later
    /// re-parsed as JSON, e.g., as a configJson string property).
    /// </summary>
    private static string SerializeEnumerableForTemplate(IEnumerable<object?> values)
    {
        try
        {
            return JsonSerializer.Serialize(values, JsonHelperOptions);
        }
        catch (Exception)
        {
            return "[]";
        }
    }

    /// <summary>
    /// {{flatMap COLL 'nested.path'}} core (R7 Wave 11 task 113). For each item in COLL,
    /// resolve the dotted-path. If the resolved value is enumerable (and not a string),
    /// expand its elements into the result; otherwise add as a single element. Null
    /// resolved values are skipped. Eliminates the need for inline lambda syntax.
    /// </summary>
    /// <example>
    /// For <c>start.categories = [{ items: [{regardingName: "Acme"}, {regardingName: "Beta"}] }, { items: [{regardingName: "Gamma"}] }]</c>
    /// the expression <c>{{flatMap start.categories 'items.regardingName'}}</c> yields
    /// <c>["Acme", "Beta", "Gamma"]</c>.
    /// </example>
    private static IEnumerable<object?> FlatMapField(object? collection, string? nestedPath)
    {
        if (collection is null || collection is UndefinedBindingResult || string.IsNullOrEmpty(nestedPath))
            return Array.Empty<object?>();

        if (collection is string)
            return Array.Empty<object?>();

        if (collection is not IEnumerable enumerable)
            return Array.Empty<object?>();

        var pathParts = nestedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<object?>();

        // For each top-level item, walk the path. Whenever we encounter a list mid-walk,
        // recursively descend into each element with the REMAINING path. This is the
        // "expand at list boundaries" semantic that distinguishes flatMap from
        // ResolveDottedPath's lookup-only behavior.
        foreach (var item in enumerable)
        {
            CollectFlatMapValues(item, pathParts, 0, result);
        }
        return result;
    }

    /// <summary>
    /// Recursive helper for FlatMapField. Walks <paramref name="pathParts"/> from
    /// <paramref name="index"/> on <paramref name="current"/>. When the current value
    /// is a non-string IEnumerable, expands each element and recurses on the same
    /// path index (so the list boundary is transparent). When path is exhausted,
    /// appends the value to <paramref name="output"/>.
    /// </summary>
    private static void CollectFlatMapValues(object? current, string[] pathParts, int index, List<object?> output)
    {
        if (current is null || current is UndefinedBindingResult) return;

        // Lists encountered mid-walk: expand each element + recurse at same index.
        if (current is not string && current is IEnumerable e && current is not IDictionary
            && !(current is IDictionary<string, object?>) && !(current is JsonElement))
        {
            foreach (var inner in e)
            {
                CollectFlatMapValues(inner, pathParts, index, output);
            }
            return;
        }

        if (index >= pathParts.Length)
        {
            output.Add(current);
            return;
        }

        var part = pathParts[index];
        object? next = null;
        switch (current)
        {
            case IDictionary<string, object?> dict:
                next = dict.TryGetValue(part, out var v) ? v : null;
                break;
            case IDictionary nonGenericDict when nonGenericDict.Contains(part):
                next = nonGenericDict[part];
                break;
            case JsonElement je when je.ValueKind == JsonValueKind.Object && je.TryGetProperty(part, out var prop):
                next = ConvertJsonElement(prop);
                break;
        }

        if (next is null) return;
        CollectFlatMapValues(next, pathParts, index + 1, output);
    }

    /// <summary>
    /// Converts an enumerable value into a comma-separated string suitable for FetchXML
    /// `operator='in'` clauses. Returns empty string for null, unresolved bindings, or
    /// non-enumerable scalars (defensive — same graceful-degradation contract as the
    /// `default` helper). Strings are treated as scalars (not as enumerable of char).
    /// </summary>
    private static string JoinIds(object? value)
    {
        if (value is null || value is UndefinedBindingResult)
        {
            return string.Empty;
        }

        // Treat string as a scalar, not as IEnumerable<char>
        if (value is string)
        {
            return string.Empty;
        }

        if (value is IEnumerable enumerable)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
            {
                parts.Add(item?.ToString() ?? string.Empty);
            }

            return string.Join(",", parts);
        }

        // Non-enumerable scalar (number, bool, object) — caller likely passed wrong shape.
        return string.Empty;
    }

    /// <summary>
    /// Returns true if the value is a non-null, non-empty, resolved binding.
    /// Handlebars.NET represents unresolved bindings as <see cref="UndefinedBindingResult"/>;
    /// these must be treated as empty for the `default` helper to fall back to 'Y'.
    /// </summary>
    private static bool IsNonEmptyValue(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is UndefinedBindingResult)
        {
            return false;
        }

        return !string.IsNullOrEmpty(value.ToString());
    }

    /// <inheritdoc />
    public string Render(string template, IDictionary<string, object?> context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        if (context == null || context.Count == 0)
        {
            // No variables to substitute, but still need to handle any {{}} that might be there
            // They'll just render as empty strings
            context = new Dictionary<string, object?>();
        }

        try
        {
            var compiled = _handlebars.Compile(template);
            var result = compiled(context);

            _logger.LogDebug(
                "Rendered template with {VariableCount} context variables",
                context.Count);

            return result;
        }
        catch (HandlebarsException ex)
        {
            _logger.LogWarning(
                ex,
                "Template rendering failed, returning original template");

            // Return original template on error rather than throwing
            return template;
        }
    }

    /// <inheritdoc />
    public string Render<T>(string template, T context) where T : class
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        if (context == null)
        {
            return Render(template, new Dictionary<string, object?>());
        }

        try
        {
            var compiled = _handlebars.Compile(template);
            var result = compiled(context);

            _logger.LogDebug(
                "Rendered template with context type {ContextType}",
                typeof(T).Name);

            return result;
        }
        catch (HandlebarsException ex)
        {
            _logger.LogWarning(
                ex,
                "Template rendering failed for type {ContextType}, returning original template",
                typeof(T).Name);

            return template;
        }
    }

    /// <summary>
    /// Converts a JsonElement to a Handlebars-traversable object hierarchy.
    /// Handlebars.NET uses reflection to traverse properties, but JsonElement is a struct
    /// whose members are ValueKind/GetString/etc. — NOT the JSON property names.
    /// This converts to Dictionary/List/primitive types that Handlebars can navigate.
    /// </summary>
    public static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => ConvertJsonElement(p.Value),
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList() as object,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <inheritdoc />
    public bool HasVariables(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return false;
        }

        return VariablePattern.IsMatch(template);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetVariableNames(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return Array.Empty<string>();
        }

        var matches = VariablePattern.Matches(template);
        var variables = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            // Group 1 is the variable name (without the braces)
            var variableName = match.Groups[1].Value.Trim();

            // For nested variables like "node.output.field", return the root
            // since that's what needs to be in the context
            var rootVariable = variableName.Split('.')[0];
            variables.Add(rootVariable);
        }

        return variables.ToList();
    }
}
