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
