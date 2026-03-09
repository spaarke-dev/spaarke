using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Static utility that extracts <c>$ref</c> entries from JPS (JSON Prompt Schema) JSON.
/// Parses only the <c>scopes</c> section for performance — does not deserialize the full schema.
/// </summary>
/// <remarks>
/// This is a static class with no DI registration (ADR-010: DI minimalism).
/// Knowledge refs use the <c>knowledge:{name}</c> prefix convention; skill refs use <c>skill:{name}</c>.
/// </remarks>
public static class JpsRefResolver
{
    private const string KnowledgePrefix = "knowledge:";
    private const string SkillPrefix = "skill:";

    /// <summary>
    /// Extracts knowledge references from the <c>scopes.$knowledge</c> array in a JPS JSON string.
    /// Each entry is expected to have a <c>$ref</c> property with a <c>knowledge:</c> prefix
    /// and an optional <c>as</c> label.
    /// </summary>
    /// <param name="jpsJson">Raw JPS JSON string. May be null, empty, or non-JPS content.</param>
    /// <returns>
    /// A list of (Name, Label) tuples where Name is the knowledge record name (prefix stripped)
    /// and Label is the contextual label from the <c>as</c> property (e.g., "reference", "definitions").
    /// Returns an empty list if input is null, empty, or does not contain JPS knowledge refs.
    /// </returns>
    public static IReadOnlyList<(string Name, string? Label)> ExtractKnowledgeRefs(string? jpsJson)
    {
        if (!TryGetScopesElement(jpsJson, out var scopesElement))
            return [];

        if (!scopesElement.TryGetProperty("$knowledge", out var knowledgeArray)
            || knowledgeArray.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<(string Name, string? Label)>();

        foreach (var entry in knowledgeArray.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            if (!entry.TryGetProperty("$ref", out var refElement)
                || refElement.ValueKind != JsonValueKind.String)
                continue;

            var refValue = refElement.GetString();
            if (refValue is null || !refValue.StartsWith(KnowledgePrefix, StringComparison.Ordinal))
                continue;

            var name = refValue[KnowledgePrefix.Length..];
            if (string.IsNullOrEmpty(name))
                continue;

            string? label = null;
            if (entry.TryGetProperty("as", out var asElement)
                && asElement.ValueKind == JsonValueKind.String)
            {
                label = asElement.GetString();
            }

            results.Add((name, label));
        }

        return results;
    }

    /// <summary>
    /// Extracts skill references from the <c>scopes.$skills</c> array in a JPS JSON string.
    /// Each entry is expected to have a <c>$ref</c> property with a <c>skill:</c> prefix.
    /// </summary>
    /// <param name="jpsJson">Raw JPS JSON string. May be null, empty, or non-JPS content.</param>
    /// <returns>
    /// A list of skill record names (prefix stripped).
    /// Returns an empty list if input is null, empty, or does not contain JPS skill refs.
    /// </returns>
    public static IReadOnlyList<string> ExtractSkillRefs(string? jpsJson)
    {
        if (!TryGetScopesElement(jpsJson, out var scopesElement))
            return [];

        if (!scopesElement.TryGetProperty("$skills", out var skillsArray)
            || skillsArray.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<string>();

        foreach (var entry in skillsArray.EnumerateArray())
        {
            // Skills can be objects with $ref or plain strings — handle both
            string? refValue = null;

            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("$ref", out var refElement)
                && refElement.ValueKind == JsonValueKind.String)
            {
                refValue = refElement.GetString();
            }
            else if (entry.ValueKind == JsonValueKind.String)
            {
                refValue = entry.GetString();
            }

            if (refValue is null || !refValue.StartsWith(SkillPrefix, StringComparison.Ordinal))
                continue;

            var name = refValue[SkillPrefix.Length..];
            if (!string.IsNullOrEmpty(name))
                results.Add(name);
        }

        return results;
    }

    /// <summary>
    /// Attempts to parse the JSON and navigate to the <c>scopes</c> property.
    /// Returns false for null, empty, whitespace, or non-object JSON without throwing.
    /// </summary>
    private static bool TryGetScopesElement(string? jpsJson, out JsonElement scopesElement)
    {
        scopesElement = default;

        if (string.IsNullOrWhiteSpace(jpsJson))
            return false;

        // Quick guard: JPS must start with '{' — skip non-JSON strings cheaply
        if (jpsJson.AsSpan().TrimStart()[0] != '{')
            return false;

        try
        {
            using var doc = JsonDocument.Parse(jpsJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!doc.RootElement.TryGetProperty("scopes", out var scopes)
                || scopes.ValueKind != JsonValueKind.Object)
                return false;

            // Clone to detach from the disposed JsonDocument
            scopesElement = scopes.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
