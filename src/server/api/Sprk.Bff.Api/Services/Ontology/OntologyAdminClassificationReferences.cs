using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ontology;

/// <summary>
/// Finds the triage categories a policy version's rule body names (ontology platform R1 task 103; D-22, FR-59). A rule body names a
/// category by its row id, in the <c>sprk_triagecategory</c> filter of an Existence or Threshold clause: <c>"sprk_triagecategory":
/// ["8b62dd84-...", "8d62dd84-..."]</c>, a single id, or an operator object (<c>{"&lt;&gt;": "..."}</c>). Rules reference categories BY ID, so a
/// rename never breaks an evaluation; it does change the name the classifier is held to (the constrained-decoding enum, NFR-09) and the
/// words a rule is read back in (<c>RuleBodyDescriber</c>), which is why a rename is refused while any version reads the category.
/// </summary>
/// <remarks>
/// <para><b>Why every string, not just the <c>sprk_triagecategory</c> key.</b> A GUID-valued string anywhere in the body counts. The
/// refusal is a safety check: reading too much only blocks a rename that would have been harmless, while reading too little would let a
/// rename through that a rule depends on. It does not need the compiler's grammar, so a future clause shape that names a category
/// elsewhere is still caught.</para>
/// <para><b>An unreadable body fails closed.</b> A body that is not valid JSON is scanned as text for any GUID (hyphenated or not), so a
/// damaged body still blocks the rename of a category it appears to name.</para>
/// <para><b>A category named by its NAME also counts (K3, task 103 fix round).</b> A JSON string VALUE equal to a category's current name
/// (trimmed, case-insensitive) is a reference, alongside the id scan. Property names are not values and are not matched. An unreadable
/// body is scanned as text for the name (case-insensitive substring), which over-reads, in the safe direction.</para>
/// </remarks>
internal static class OntologyAdminClassificationReferences
{
    private static readonly Regex GuidText = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every GUID the rule body names, in any format <see cref="Guid.TryParse(string?, out Guid)"/> reads. Empty for a null or blank body.</summary>
    internal static IReadOnlySet<Guid> GuidsIn(string? ruleBody)
    {
        var found = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            return found;
        }

        try
        {
            using var doc = JsonDocument.Parse(ruleBody);
            Walk(doc.RootElement, found);
        }
        catch (JsonException)
        {
            foreach (Match match in GuidText.Matches(ruleBody))
            {
                if (Guid.TryParse(match.Value, out var id))
                {
                    found.Add(id);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The categories (by id) that <paramref name="ruleBody"/> names, either by id (<see cref="GuidsIn"/>) or by a string value equal to the
    /// category's current name. <paramref name="categories"/> maps each category id to its current name.
    /// </summary>
    internal static IReadOnlySet<Guid> CategoriesNamedIn(string? ruleBody, IReadOnlyDictionary<Guid, string> categories)
    {
        var found = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            return found;
        }

        foreach (var id in GuidsIn(ruleBody))
        {
            if (categories.ContainsKey(id))
            {
                found.Add(id);
            }
        }

        HashSet<string>? values = null;
        try
        {
            using var doc = JsonDocument.Parse(ruleBody);
            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectStrings(doc.RootElement, values);
        }
        catch (JsonException)
        {
            values = null;
        }

        foreach (var (id, name) in categories)
        {
            var wanted = name.Trim();
            if (wanted.Length == 0)
            {
                continue;
            }

            var named = values is not null
                ? values.Contains(wanted)
                : ruleBody.Contains(wanted, StringComparison.OrdinalIgnoreCase);
            if (named)
            {
                found.Add(id);
            }
        }

        return found;
    }

    private static void CollectStrings(JsonElement element, HashSet<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (element.GetString()?.Trim() is { Length: > 0 } text)
                {
                    values.Add(text);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, values);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, values);
                }

                break;
        }
    }

    private static void Walk(JsonElement element, HashSet<Guid> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (Guid.TryParse(element.GetString(), out var id))
                {
                    found.Add(id);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, found);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, found);
                }

                break;
        }
    }
}
